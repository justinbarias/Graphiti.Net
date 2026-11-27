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

A result that holds an int that a C# long cannot hold has the mark `"gap": "F41"` (GAPS F41, the limit of RULES.md R4).
The port throws OverflowException for such a case, and the test asserts that it still does.

The error text is str(ValidationError) without the input value, the input type, and the URL line (R25).

It writes tests/Port.Evaluation/values/custom_types.json, and custom_types_2.json with the input classes that the review
of packet P7f found missing: more classes (docstrings that inspect.cleandoc changes, max_length schemas, titles of
names that are not ASCII or end in `_`, field descriptions at the edges, names that differ only by case, names for
which pydantic only warns), the inputs of POOL2 for each class of the first file (`more_cases`), and the field names
that pydantic refuses at class creation with the Python error of each D26 form (`refused`). A value file stays under
1.5 MB. --check writes nothing and exits 1 if a file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import importlib.util
import json
import os
import random
import re
import sys
import types
import warnings
from pathlib import Path

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
FIXTURES = ROOT / 'port' / 'judge' / 'host-python' / 'fixtures.py'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'custom_types.json'
OUT2 = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'custom_types_2.json'

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


def validate(cls: type[BaseModel], data: dict) -> dict:
    try:
        dumped = cls(**data).model_dump()
    except ValidationError as e:
        return {'err': {'errors': [[list(er['loc']), er['type'], er['msg']] for er in e.errors()], 'text': text_of(e)}}
    if contains_big(dumped):
        return {'ok': dumped, 'gap': 'F41'}
    return {'ok': dumped}


def inputs(spec: dict, pool: list = POOL, gen: random.Random = rng) -> list[dict]:
    out = [{}, base_data(spec)]
    for f in spec['fields']:
        for value in pool:
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
            r = gen.random()
            if r < 0.3:
                continue
            data[f['name']] = valid_value(f) if r < 0.6 else gen.choice(pool)
        if gen.random() < 0.3:
            data['extra'] = gen.choice(pool)
        out.append(data)
    return out


# --- The second value file -------------------------------------------------------------------------------------------
#
# The input classes that review B of packet P7f found missing, and the differences that the fix of the review closed:
# the schema description (inspect.cleandoc), the maxLength key, the field titles, and the field names that pydantic
# refuses. A case whose result holds an int that a C# long cannot hold has the mark "gap": "F41" (GAPS F41): the port
# throws OverflowException there, and the test asserts that it still does.

POOL2 = [
    2**63 - 1, -(2**63), 16777217, 2**53 + 1, 10**20, -(10**20), -1, -1.0, 2.0,
    '+5', '+1.5', '007', '-0', '\t12\n', ' 7 ', '1.000', '.5', '5.', '1E3', ' 1.5 ', 'Infinity', '-inf', 'iNf',
    '9223372036854775807', '-9223372036854775808', '100000000000000000000',
    'y', 'n', 'Y', 'N', 'yes', 'YES', 'Yes', 'no', 'NO', 'true', 'TRUE', 'false', 'FALSE', 'True', 'on', 'On', 'OFF',
    't', 'T', 'F', ' true ',
    ['+5', '007'], [2**63 - 1, -(2**63)], [10**20], [16777217, 2**53 + 1], ['y', 'N', 'true'], ['Infinity', '.5'],
    ['\ud800'], [[]], [-1.0, 2.0],
]

rng2 = random.Random(20261128)


def specs2() -> list[dict]:
    docs = ['   ', '\n', ' Lead space.', 'Line one.\n    Line two.', '\n    A person.\n\n    More text.\n    ', 'a\tb',
            'Line one.\r\nLine two.\r\n', 'One line.', '\t\tTabbed start.\n\tSecond line.', '\n\n  First.\n\n',
            '  \n  \n', 'First.\n  Two spaces.\n    Four spaces.\n', 'x\n\ty\n        z', '　Ideographic space.',
            'Café.\n  é indented.', 'Form\x0cfeed\x0b.\n\x1c sep.']
    specs = [{'name': f'Doc{i}', 'doc': doc, 'fields': [field('value', 'String', 'A value')]} for i, doc in enumerate(docs)]
    specs.append({'name': 'MaxLengths', 'doc': 'Text fields with max_length.', 'fields': [
        field('zero_required', 'String', None, True, 0), field('zero_optional', 'String', 'Only empty', False, 0),
        field('big_required', 'String', 'A large limit', True, 2147483647), field('big_optional', 'String', None, False, 2147483647),
        field('ten', 'String', None, False, 10), field('plain', 'String', None, False, None),
    ]})
    specs.append({'name': 'Titles2', 'doc': None, 'fields': [
        field('trailing_', 'String'), field('type_', 'Integer'), field('école_x', 'String'), field('größe', 'Number'),
        field('naïve_date', 'String'), field('ßtraße', 'Boolean'), field('ǆx', 'String'), field("it's", 'String'),
        field('ΑΣ_end', 'String'), field('a__b_', 'StringList'), field('x1y2', 'IntegerList'), field(' padded ', 'String'),
        field('ǅungla', 'NumberList'), field('ﬁx', 'BooleanList'), field('ǰx', 'String'), field('東京_x', 'String'),
    ]})
    specs.append({'name': 'Descriptions', 'doc': 'Field descriptions at the edges.', 'fields': [
        field('a', 'String', '', True), field('b', 'Integer', '  padded  ', True), field('c', 'Number', '', False),
        field('d', 'BooleanList', ' lead', False), field('e', 'StringList', 'trail ', False),
    ]})
    specs.append({'name': 'CaseNames', 'doc': 'Two fields whose names differ only by case.', 'fields': [
        field('a', 'Integer', None, False), field('A', 'Integer', None, False), field('age', 'Integer', None, True),
    ]})
    specs.append({'name': 'WarnedNames', 'doc': 'Names for which pydantic only warns.', 'fields': [
        field('schema', 'String'), field('json', 'Integer', None, True), field('copy', 'Boolean'),
        field('dict', 'StringList'), field('model_dump_x', 'String'), field('construct', 'Number'),
    ]})
    return specs


def inputs2(spec: dict, seen: set | None = None) -> list[dict]:
    """POOL2 in each field. With `seen`, only in the first field of each mix of kind, required flag, and max_length."""
    out = []
    for f in spec['fields']:
        if seen is not None:
            key = (f['kind'], f['required'], f['max_length'] is not None)
            if key in seen:
                continue
            seen.add(key)
        for value in POOL2:
            out.append({**base_data(spec), f['name']: value})
        if f['name'].upper() != f['name']:
            out.append({**base_data(spec), f['name'].upper(): valid_value(f)})
            out.append({**base_data(spec), f['name'].upper(): POOL2[0], f['name']: valid_value(f)})
    return out


# The field names that pydantic refuses at class creation, in the four forms of decision D26.
REFUSED = ['_x', '__x', '_', '__x__', '__doc__', '_abc_impl', 'model_config', 'model_dump', 'model_dump_json',
           'model_validate', 'model_validate_json', 'model_validate_strings']


def refused() -> list[dict]:
    out = []
    for name in REFUSED:
        forms = []
        for required in (True, False):
            for described in (True, False):
                spec = {'name': 'Refused', 'doc': None,
                        'fields': [field(name, 'String', 'd' if described else None, required)]}
                try:
                    with warnings.catch_warnings():
                        warnings.simplefilter('ignore')
                        cls = build(spec)
                    result = 'no field' if name not in cls.model_fields else 'field'
                except Exception as e:  # noqa: BLE001 - the type of the error is what the file records
                    result = type(e).__name__
                forms.append([required, described, result])
        out.append({'name': name, 'forms': forms})
    return out


# --- Recording -------------------------------------------------------------------------------------------------------


def cases_of(cls: type[BaseModel], datas: list[dict]) -> list[dict]:
    return [{'data': data, **validate(cls, data)} for data in datas]


def record(cls: type[BaseModel], spec: dict, datas: list[dict] | None = None) -> dict:
    return {
        'spec': spec,
        'doc': cls.__doc__,
        'model_fields': [[name, _field_is_required(cls, name), _field_max_length(cls, name)] for name in cls.model_fields],
        'schema': json.dumps(cls.model_json_schema()),
        'cases': cases_of(cls, inputs(spec) if datas is None else datas),
    }


def file_text(entries: list[dict], extra: dict) -> str:
    import pydantic

    lines = [json.dumps(e, ensure_ascii=True, separators=(',', ':')) for e in entries]
    head = '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","pydantic":"' + pydantic.VERSION + '",'
    tail = ''.join(f'"{key}":{json.dumps(value, ensure_ascii=True, separators=(",", ":"))},\n' for key, value in extra.items())
    return head + tail + '"types":[\n' + ',\n'.join(lines) + '\n]}\n'


def main() -> int:
    entries = []
    classes = []
    for cls in load_fixtures():
        spec = spec_of(cls)
        assert build(spec).model_json_schema() == cls.model_json_schema(), cls.__name__
        entries.append(record(cls, spec))
        classes.append((cls, spec))
    for spec in coverage_specs() + random_specs(8):
        cls = build(spec)
        entries.append(record(cls, spec))
        classes.append((cls, spec))

    # The second file: the new classes with all inputs, and the inputs of POOL2 for each class of the first file.
    entries2 = []
    for spec in specs2():
        with warnings.catch_warnings():
            warnings.simplefilter('ignore')
            cls = build(spec)
        datas = [{}, {'value': 'x'}] if spec['name'].startswith('Doc') else inputs(spec, POOL, rng2) + inputs2(spec)
        entries2.append(record(cls, spec, datas))
    seen: set = set()
    more = [{'name': spec['name'], 'cases': cases_of(cls, inputs2(spec, seen))} for cls, spec in classes]
    more = [m for m in more if m['cases']]

    outputs = [(OUT, file_text(entries, {}), entries, []), (OUT2, file_text(entries2, {'refused': refused(), 'more_cases': more}), entries2, more)]
    stale = False
    for path, text, written, extra in outputs:
        count = sum(len(e['cases']) for e in written + extra)
        size = len(text.encode('utf-8'))
        assert size < 1_572_864, (path.name, size)
        if '--check' in sys.argv[1:]:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                print(f'{path.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
                stale = True
            else:
                print(f'{path.relative_to(ROOT)}: current, {len(written)} types and {count} cases')
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        print(f'{path.relative_to(ROOT)}: {len(written)} types and {count} cases, {size} bytes')
    return 1 if stale else 0


if __name__ == '__main__':
    sys.exit(main())
