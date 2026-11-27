"""Records what pydantic gives for custom entity and edge types, for the evaluation tests (port/RULES.md, R7, R15, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_custom_types.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_custom_types.py --check

Debt T18, decision D26. Upstream takes a custom type as a pydantic class that the caller wrote, and reads four things
from it: `__doc__`, `model_fields` (the keys, `is_required()`, and the `max_length` of the metadata),
`model_json_schema()`, and `type_model(**data)`. The port has the public GraphTypeDefinition with a closed set of field
kinds, which builds the internal GraphTypeDescriptor. This tool builds a pydantic class for each type spec of a list
and records the four things. The C# test builds the same type through GraphTypeDefinition and compares each value.

A spec has a name, a docstring or null, and fields. A field has a name, a kind (the member name of the public
GraphFieldKind), a description or null, a required flag, and a max_length or null. A required field is
`x: T = Field(...)`, and a field that is not required is `x: T | None = Field(default=None, ...)`, as decision D26
gives it. The specs are:

- the five classes of port/judge/host-python/fixtures.py. The tool reads the spec of each one from the class, builds
  the class again from the spec, and checks that both give the same schema. It records the class of the file.
- one class for each kind, with a field for each mix of a description and the required flag, and for a text also each
  mix with max_length,
- classes from a fixed seed that mix kinds, names, and docstrings.

For each class the tool records `__doc__`, the field list as attribute_utils reads it (`_field_is_required` and
`_field_max_length`), the text `json.dumps(model_json_schema())`, and `type_model(**data)` for many inputs: the result
of `model_dump()`, or the errors and the text of the ValidationError. The inputs are each value of a pool in each field,
a missing key, an extra key, and random mixes. The pool has a wrong type for each kind, a bool, an int, a float, a
numeric text, None, an empty list, a list with a wrong item, a text over max_length, non-ASCII text, and a lone
surrogate. The values are the JSON values of a model reply.

Pydantic writes `"maxLength"` into the schema of a text field with max_length, and the port cannot (PydanticType has no
member for it). For such a class the tool also records the schema text without the key, and the test compares with
that text. The tool leaves out a docstring that `inspect.cleandoc` changes, because the port writes one text for both
(decision D26), a result that holds an int that a C# long cannot hold (the limit of RULES.md R4), a field name that is
not ASCII (the title rule of PydanticSchema is for ASCII text), and a field name that ends in `_` (pydantic strips the
space at the end of the title, and PydanticSchema does not).

The error text is str(ValidationError) without the input value, the input type, and the URL line (R25).

It writes tests/Port.Evaluation/values/custom_types.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import importlib.util
import inspect
import json
import os
import random
import re
import sys
import types
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
FIXTURES = ROOT / 'port' / 'judge' / 'host-python' / 'fixtures.py'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'custom_types.json'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'record_custom_types: needs Python 3.12, found {sys.version.split()[0]}')

sys.path.insert(0, str(UPSTREAM))

from pydantic import BaseModel, Field, ValidationError  # noqa: E402

from graphiti_core.utils.maintenance.attribute_utils import _field_is_required, _field_max_length  # noqa: E402

SCALARS = {'String': str, 'Integer': int, 'Number': float, 'Boolean': bool}
KINDS = {**SCALARS, **{name + 'List': list[t] for name, t in SCALARS.items()}}
LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1

rng = random.Random(20261127)


# --- Classes ---------------------------------------------------------------------------------------------------------


def build(spec: dict) -> type[BaseModel]:
    """The class statement of a spec: `x: T = Field(...)` or `x: T | None = Field(default=None, ...)`."""
    annotations: dict = {}
    namespace: dict = {'__module__': 'custom_types', '__qualname__': spec['name'], '__annotations__': annotations}
    if spec['doc'] is not None:
        namespace['__doc__'] = spec['doc']
    for field in spec['fields']:
        kind = KINDS[field['kind']]
        options = {}
        if field['description'] is not None:
            options['description'] = field['description']
        if field['max_length'] is not None:
            options['max_length'] = field['max_length']
        if field['required']:
            annotations[field['name']] = kind
            if options:
                namespace[field['name']] = Field(**options)
        else:
            annotations[field['name']] = kind | None
            namespace[field['name']] = Field(default=None, **options)
    return types.new_class(spec['name'], (BaseModel,), {}, lambda ns: ns.update(namespace))


def spec_of(cls: type[BaseModel]) -> dict:
    """The spec of a class that uses only the field forms of decision D26."""
    fields = []
    for name, info in cls.model_fields.items():
        annotation = info.annotation
        required = info.is_required()
        if not required:
            assert info.default is None and info.default_factory is None, (cls.__name__, name)
            args = [a for a in annotation.__args__ if a is not type(None)]
            assert len(args) == 1 and len(annotation.__args__) == 2, (cls.__name__, name)
            annotation = args[0]
        kind = next(k for k, t in KINDS.items() if t == annotation)
        fields.append({'name': name, 'kind': kind, 'description': info.description, 'required': required,
                       'max_length': _field_max_length(cls, name)})
    return {'name': cls.__name__, 'doc': cls.__doc__, 'fields': fields}


def load_fixtures() -> list[type[BaseModel]]:
    module_spec = importlib.util.spec_from_file_location('fixtures', FIXTURES)
    module = importlib.util.module_from_spec(module_spec)
    module_spec.loader.exec_module(module)
    return [module.Person, module.Product, module.Company, module.Likes, module.WorksAt]


def field(name, kind, description=None, required=False, max_length=None) -> dict:
    return {'name': name, 'kind': kind, 'description': description, 'required': required, 'max_length': max_length}


def coverage_specs() -> list[dict]:
    docs = ['One kind of field in each mix of options.', None, '', 'A type with "quotes", \'apostrophes\', é and 東京.',
            'First line.\nSecond line with no indent.', 'Ends with spaces.  ', 'Back\\slash inside.', None]
    specs = []
    for i, kind in enumerate(KINDS):
        stem = kind.lower()
        fields = [
            field(f'{stem}_required_described', kind, f'A required {kind} field', True),
            field(f'{stem}_required', kind, None, True),
            field(f'{stem}_optional_described', kind, f'An optional {kind} field: "quoted", é', False),
            field(f'{stem}_optional', kind, None, False),
        ]
        if kind == 'String':
            fields += [
                field('limited_required', kind, 'At most 5 code points', True, 5),
                field('limited_optional', kind, None, False, 3),
                field('limited_zero', kind, 'Only the empty text', False, 0),
                field('limited_one', kind, None, False, 1),
            ]
        specs.append({'name': f'{kind}Kinds', 'doc': docs[i], 'fields': fields})
    specs.append({'name': 'Empty', 'doc': 'A type with no field.', 'fields': []})
    specs.append({'name': 'Titles', 'doc': None, 'fields': [
        field('x', 'String'), field('home_city', 'String', 'The city'), field('URL', 'String'),
        field('camelCase', 'Integer'), field('with2digits', 'Number'), field('a__b', 'Boolean'),
        field('mid_dle', 'StringList'), field('X1y2Z', 'IntegerList'), field('model_name_x', 'NumberList'),
    ]})
    return specs


NAMES = ['age', 'score', 'tags', 'is_active', 'nickname', 'rating', 'counts', 'flags', 'city', 'email', 'phone_number',
         'weights', 'title', 'level', 'aliases']
DESCRIPTIONS = [None, None, 'A plain description', 'The value, for example 42', 'Ünïcödé text: 東京 😀',
                'Text with "quotes" and \\ backslash', 'Line one\nline two']
DOCS = [None, 'A random type.', 'A random type with a longer docstring that names the entity in the text.', '']


def random_specs(count: int) -> list[dict]:
    specs = []
    for i in range(count):
        names = rng.sample(NAMES, rng.randint(1, 6))
        fields = []
        for name in names:
            kind = rng.choice(list(KINDS))
            max_length = rng.choice([None, None, 2, 8, 20]) if kind == 'String' else None
            fields.append(field(name, kind, rng.choice(DESCRIPTIONS), rng.random() < 0.4, max_length))
        specs.append({'name': f'Random{i}', 'doc': rng.choice(DOCS), 'fields': fields})
    return specs


# --- Inputs ----------------------------------------------------------------------------------------------------------

POOL = [
    '', 'abc', 'ab', 'abcde', 'abcdef', 'Ünïcödé 東京', '😀😀😀', '😀😀😀😀😀😀', 'x' * 30, '\ud800x', 'line\nbreak',
    ' 12 ', '12', '-3', '1_000', '1.0', '1.5', '1e3', '0x10', 'inf', 'nan', 'abc1',
    'yes', 'no', 'off', 'ON', 'TRUE', 'f', 't', '0', '1', '2',
    0, 1, -7, 2, 2**40, 1.0, 0.0, -0.0, 1.5, 2.5e-5, 1e20, float('inf'), float('nan'),
    True, False, None,
    [], ['a'], ['a', 'b'], [1, 2], ['1', '2'], [1.5], [True, False], ['yes', 0], ['a', 1], [None], [[1]], [{'a': 1}],
    {}, {'a': 1},
]

VALID = {'String': 'abc', 'Integer': 7, 'Number': 2.5, 'Boolean': True,
         'StringList': ['a', 'b'], 'IntegerList': [1, 2], 'NumberList': [0.5], 'BooleanList': [False]}


def valid_value(f: dict):
    value = VALID[f['kind']]
    if f['max_length'] is not None:
        value = value[: f['max_length']]
    return value


def base_data(spec: dict) -> dict:
    return {f['name']: valid_value(f) for f in spec['fields'] if f['required']}


def contains_big(v) -> bool:
    if isinstance(v, bool):
        return False
    if isinstance(v, int):
        return not LONG_MIN <= v <= LONG_MAX
    if isinstance(v, list):
        return any(contains_big(x) for x in v)
    if isinstance(v, dict):
        return any(contains_big(x) for x in v.values())
    return False


def text_of(e: ValidationError) -> str:
    """str(e) without the input value, the input type, and the URL line."""
    lines = []
    for line in str(e).split('\n'):
        if line.startswith('    For further information visit '):
            continue
        lines.append(re.sub(r', input_value=.*, input_type=[A-Za-z_]+\]$', ']', line))
    return '\n'.join(lines)


def validate(cls: type[BaseModel], data: dict) -> dict | None:
    try:
        dumped = cls(**data).model_dump()
    except ValidationError as e:
        return {'err': {'errors': [[list(er['loc']), er['type'], er['msg']] for er in e.errors()], 'text': text_of(e)}}
    if contains_big(dumped):
        return None
    return {'ok': dumped}


def inputs(spec: dict) -> list[dict]:
    out = [{}, base_data(spec)]
    for f in spec['fields']:
        for value in POOL:
            out.append({**base_data(spec), f['name']: value})
        if f['required']:
            data = base_data(spec)
            del data[f['name']]
            out.append(data)
    out.append({**base_data(spec), 'extra_key': 1, 'Extra': None})
    out.append({**{f['name']: valid_value(f) for f in spec['fields']}, 'unknown': 'ignored'})
    for _ in range(20):
        data = {}
        for f in spec['fields']:
            r = rng.random()
            if r < 0.3:
                continue
            data[f['name']] = valid_value(f) if r < 0.6 else rng.choice(POOL)
        if rng.random() < 0.3:
            data['extra'] = rng.choice(POOL)
        out.append(data)
    return out


# --- Recording -------------------------------------------------------------------------------------------------------


def without_max_length(value):
    if isinstance(value, dict):
        return {k: without_max_length(v) for k, v in value.items() if k != 'maxLength'}
    if isinstance(value, list):
        return [without_max_length(v) for v in value]
    return value


def record(cls: type[BaseModel], spec: dict) -> dict:
    assert spec['doc'] is None or inspect.cleandoc(spec['doc']) == spec['doc'] or spec['doc'] == '', spec['name']
    assert all(f['name'].isascii() and not f['name'].endswith('_') for f in spec['fields']), spec['name']
    schema = cls.model_json_schema()
    entry = {
        'spec': spec,
        'doc': cls.__doc__,
        'model_fields': [[name, _field_is_required(cls, name), _field_max_length(cls, name)] for name in cls.model_fields],
        'schema': json.dumps(schema),
    }
    if without_max_length(schema) != schema:
        entry['schema_without_max_length'] = json.dumps(without_max_length(schema))
    cases = []
    for data in inputs(spec):
        result = validate(cls, data)
        if result is not None:
            cases.append({'data': data, **result})
    entry['cases'] = cases
    return entry


def main() -> int:
    entries = []
    for cls in load_fixtures():
        spec = spec_of(cls)
        assert build(spec).model_json_schema() == cls.model_json_schema(), cls.__name__
        entries.append(record(cls, spec))
    for spec in coverage_specs() + random_specs(8):
        entries.append(record(build(spec), spec))

    lines = [json.dumps(e, ensure_ascii=True, separators=(',', ':')) for e in entries]
    import pydantic

    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","pydantic":"' + pydantic.VERSION
        + '","types":[\n' + ',\n'.join(lines) + '\n]}\n'
    )
    count = sum(len(e['cases']) for e in entries)
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(entries)} types and {count} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(entries)} types and {count} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
