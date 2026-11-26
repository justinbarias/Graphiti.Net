"""Records what pydantic gives for the Pydantic helpers of PyCompat (RULES.md R4, R7).

One file comes out of a run:

- tests/Graphiti.Net.Property.Tests/python-values.pydantic.json: the result or the errors of pydantic
  lax mode for each field type that the models of the MVP modules use, for a matrix of inputs. Also
  the result or the errors of ModelValidate and ModelDump for upstream models, and their model_fields.

    .upstream/graphiti/.venv/bin/python port/tools/generate_pydantic_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_pydantic_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12 and the pinned pydantic of the upstream environment. The file records the
versions.

A value is written in the tagged form of generate_valuemodel_values.py, with two changes:

    {"enum": "ComparisonOperator.equals"}  a member of one of the upstream enums of ENUMS below
    {"model": "DateFilter", "data": {...}} an instance of an upstream model, as the C# test builds it
                                           with ModelValidate of the data
    {"anything": true}                     a value from a default factory that differs on each run
                                           (a new UUID or the clock). The test does not compare it.

A case for a field type records pydantic's result of a one-field model `Model` with the field `x`:

    {"v": input, "r": result}              the validated value
    {"v": input, "big": "123..."}          an int that a C# long cannot hold. The helper throws
                                           OverflowException (the limit of RULES.md R4).
    {"v": input, "errors": [[loc, type, msg], ...], "text": "..."}

"text" is str(ValidationError) without the parts that hold the input (", input_value=...,
input_type=..." and the URL line). No upstream code shows the text to a model or stores it (see
ValidationException), so the C# message keeps only the title, the location, the message, and the type.
"""
from __future__ import annotations

import datetime
import enum
import json
import math
import re
import sys
from pathlib import Path
from typing import Any, Callable, Literal

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.pydantic.json'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_pydantic_values: needs Python 3.12, found {sys.version.split()[0]}')

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(ROOT / '.upstream' / 'graphiti'))

import pydantic  # noqa: E402
import pydantic_core  # noqa: E402
from pydantic import BaseModel, Field, TypeAdapter, ValidationError, create_model  # noqa: E402

from generate_valuemodel_values import Encoder, cps  # noqa: E402

from graphiti_core.ingestion.change_set import Operation  # noqa: E402
from graphiti_core.ingestion.config import ConcurrentIngestionConfig  # noqa: E402
from graphiti_core.nodes import EntityNode, EpisodeType, EpisodicNode  # noqa: E402
from graphiti_core.prompts.dedupe_nodes import NodeDuplicate, NodeResolutions  # noqa: E402
from graphiti_core.prompts.extract_edges import Edge, ExtractedEdges  # noqa: E402
from graphiti_core.prompts.models import ChatPrompt, SystemMessage, UserMessage  # noqa: E402
from graphiti_core.search.search_config import (  # noqa: E402
    EdgeReranker,
    EdgeSearchConfig,
    EdgeSearchMethod,
    NodeReranker,
    NodeSearchConfig,
    NodeSearchMethod,
    SearchConfig,
)
from graphiti_core.search.search_filters import (  # noqa: E402
    ComparisonOperator,
    DateFilter,
    PropertyFilter,
    SearchFilters,
)

ENUMS = [ComparisonOperator, EpisodeType, EdgeSearchMethod, EdgeReranker, NodeSearchMethod, NodeReranker]


class Ops(BaseModel):
    """A test model with the one field of ChangeSet that holds the discriminated union Operation."""

    operations: list[Operation] = Field(default_factory=list)


MODELS: dict[str, type[BaseModel]] = {
    'DateFilter': DateFilter,
    'PropertyFilter': PropertyFilter,
    'SearchFilters': SearchFilters,
    'EntityNode': EntityNode,
    'EpisodicNode': EpisodicNode,
    'NodeDuplicate': NodeDuplicate,
    'NodeResolutions': NodeResolutions,
    'Edge': Edge,
    'ExtractedEdges': ExtractedEdges,
    'SystemMessage': SystemMessage,
    'UserMessage': UserMessage,
    'ChatPrompt': ChatPrompt,
    'EdgeSearchConfig': EdgeSearchConfig,
    'NodeSearchConfig': NodeSearchConfig,
    'SearchConfig': SearchConfig,
    'ConcurrentIngestionConfig': ConcurrentIngestionConfig,
    'Ops': Ops,
}

# Fields whose default factory gives a new value on each run.
UNSTABLE_DEFAULTS = {('EntityNode', 'uuid'), ('EntityNode', 'created_at'), ('EpisodicNode', 'uuid'),
                     ('EpisodicNode', 'created_at')}


class Anything:
    """A value that the test does not compare."""


class PydanticEncoder(Encoder):
    def _enc(self, v):
        if isinstance(v, Anything):
            return {'anything': True}
        if isinstance(v, enum.Enum):
            assert type(v) in ENUMS, type(v)
            return {'enum': f'{type(v).__name__}.{v.name}'}
        if isinstance(v, BaseModel):
            name = type(v).__name__
            assert MODELS.get(name) is type(v), name
            return {'model': name, 'data': self.enc(v.model_dump())}
        return super()._enc(v)


def enc(v):
    e = PydanticEncoder()
    e.count(v)
    return e.enc(v)


def text_of(e: ValidationError) -> str:
    """str(e) without the input value, the input type, and the URL line."""
    lines = []
    for line in str(e).split('\n'):
        if line.startswith('    For further information visit '):
            continue
        lines.append(re.sub(r', input_value=.*, input_type=[A-Za-z_]+\]$', ']', line))
    return '\n'.join(lines)


def errors_of(e: ValidationError) -> list:
    return [[list(er['loc']), er['type'], cps(er['msg'])] for er in e.errors()]


LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1


def contains_big(v) -> bool:
    if isinstance(v, bool):
        return False
    if isinstance(v, int):
        return not LONG_MIN <= v <= LONG_MAX
    if isinstance(v, (list, tuple, set, frozenset)):
        return any(contains_big(x) for x in v)
    if isinstance(v, dict):
        return any(contains_big(x) for x in v.values())
    return False


def field_case(model: type[BaseModel], v, keeps_big: bool) -> dict:
    """One input of a one-field model `Model` with the field `x`. With keeps_big, the field type is `Any` or holds
    `Any`, which keeps a big int as the value model does (a BigInteger), so the result is a value, not an overflow."""
    case = {'v': enc(v)}
    try:
        m = model.model_validate({'x': v})
    except ValidationError as e:
        case['errors'] = errors_of(e)
        case['text'] = cps(text_of(e))
        return case
    r = m.x
    if not keeps_big and contains_big(r):
        case['big'] = str(r)
    else:
        case['r'] = enc(r)
    return case


def one_field(tp, **field) -> type[BaseModel]:
    if field:
        return create_model('Model', x=(tp, Field(**field)))
    return create_model('Model', x=(tp, ...))


# ---------------------------------------------------------------------------------------------
# Inputs
# ---------------------------------------------------------------------------------------------

def naive(*a):
    return datetime.datetime(*a)


def aware(*a, offset_minutes=0):
    return datetime.datetime(*a, tzinfo=datetime.timezone(datetime.timedelta(minutes=offset_minutes)))


COMMON = [
    None, True, False,
    0, 1, -1, 2, 7, -7, 2**31, 2**53 + 1, LONG_MAX, LONG_MIN, 2**63, LONG_MIN - 1, 2**64, 10**30, -(10**30),
    0.0, -0.0, 1.0, -1.0, 1.5, -1.5, 0.5, 2.0, 1e10, 1e15, 9.2e18, 2.0**63, -(2.0**63), 9223372036854774784.0,
    -9223372036854774784.0, 1e20, 1e300, 5e-324, float('nan'), float('inf'), float('-inf'), 1700000000.5,
    '', ' ', '0', '1', '-1', '+1', ' 1 ', '\t1\n', ' 1 ', ' 1', '\x1c1', '\x1f1', '\x0b1\x0c',
    '\u0085' + '1', '1\x00', '1_000', '_1', '1_', '1__0', '+_1', '1_0_0', '01', '007', '00', '-0', '+0', '--1',
    '1.0', '1.00', '1.', '.0', '1.5', '-1.0', '+1.0', '1.0e0', '1.01', '0.0', '1e3', '1E3', '1e-3', '1.5e3', '.5',
    '5.', '1.e5', 'e5', '1e', '1e+', 'inf', '-inf', '+inf', 'Infinity', 'INF', 'iNf', 'nan', 'NaN', '-nan', 'infinity ',
    'infinit', '0x10', '0b1', '0o7', '١٢', '１', '²', 'abc', 'true', 'True', 'TRUE', 'false',
    'False', 'yes', 'YES', 'no', 'on', 'off', 'ON', 'Off', 't', 'f', 'T', 'F', 'y', 'n', 'Y', 'N', 'tru', 'truee',
    ' true', 'true ', 'oN', 'nO', '9223372036854775807', '9223372036854775808', '-9223372036854775808',
    '-9223372036854775809', '1' * 30, '1' * 4300, '1' * 4301, '-' + '1' * 4300, '1e309', '-1e309', '1e-400',
    '1.7976931348623157e308', '0.1', '2.5', '1_000.5', '1_000e1_0', '1.5_0', '1._5', '1_.5', '0_0', '١٢٣٤٥', 'é',
    '\U0001F600', 'é', '\ud800', 'x\udfff', 'message', 'json', 'fact_triple', 'Message', '=', '<>', 'IS NULL',
    'equals', 'system', 'user', 'System', 'system ', 'reciprocal_rank_fusion', 'rrf', 'bm25',
    b'', b'1', b'1.5', b' 2 ', b'true', b'abc', b'\xff', b'\xff\xfe', b'2024-01-02', b'1700000000', b'system',
    b'message', b'\xc3\xa9',
    [], [1], [1, 2], ['a'], ['a', 'b'], [None], [[1]], ['1', 2, 3.0], [True], (1,), (), ('a', 'b'), {1}, frozenset({2}),
    {}, {'a': 1}, {'a': [1, 2]}, {'a': None, 'b': 'x'}, {1: 'a'}, {'a': {'b': 1}},
    naive(2024, 1, 2, 3, 4, 5), naive(2024, 1, 2, 3, 4, 5, 6), aware(2024, 1, 2), aware(2024, 1, 2, 3, 4, offset_minutes=330),
    aware(1, 1, 1, offset_minutes=-60), datetime.date(2024, 1, 2), datetime.date(1, 1, 1),
    ComparisonOperator.equals, ComparisonOperator.is_null, EpisodeType.message,
]

DATETIME_EXTRA = [
    '2024-01-02', '2024-01-02T03:04:05', '2024-01-02 03:04:05', '2024-01-02_03:04:05', '2024-01-02x03:04:05',
    '2024-01-02t03:04:05', '2024-01-02T03:04:05.1', '2024-01-02T03:04:05.1234567', '2024-01-02T03:04:05,5',
    '2024-01-02T03:04:05.', '2024-01-02T03:04:5', '2024-01-02T3:04:05', '2024-01-02T030405', '2024-01-02T03:04:60',
    '2024-01-02T03:04:59', '2024-01-02T03:60:00', '2024-01-02T23:59:59.999999', '2024-01-02T24:00:00',
    '2024-01-02T25:00:00', '2024-01-02T03:04:05+24:00', '2024-01-02T03:04:05+23:59', '2024-01-02T03:04:05-23:59',
    '2024-01-02T03:04:05+05:60', '2024-01-02T03:04:05-00:00', '2024-01-02T03:04:05+00:00', '2024-01-02T03:04:05+00',
    '2024-01-02T03:04:05+0', '2024-01-02T03:04:05+05:3', '2024-01-02T03:04:05 +05:30', '2024-01-02T03:04:05Zx',
    '2024-01-02T03:04:05.123Z', '2024-01-02T03:04:05.999999999', '2024-01-02T03:04:05.000000', '2024-01-02T',
    '2024-01-02T0', '2024-01-02T03', '2024-01-02T03:', '2024-01-02T03:0', '2024-01-02T03:04', '2024-01-02 ',
    '2024-01-02  ', '2024/01/02', '2024-01/02', '2024-13-01', '2024-00-01', '2024-01-00', '2024-01-32',
    '2023-02-29', '2024-02-29', '2100-02-29', '2000-02-29', '2024-04-31', '2024-12-31', 'x024-01-02', '20x4-01-02',
    '2024-x1-02', '2024-01-x2', '2024-1x-02', '2024-01-0x', '0000-01-01', '0000-01-01T00:00:00', '0000-02-29',
    '9999-12-31T23:59:59.999999', '9999-12-31T23:59:59-01:00', '9999-12-31T23:59:59+01:00',
    '0001-01-01T00:00:00+01:00', '2024-01-02T03:04:05.123456+05:30', '２０２４-01-02',
    '+2024-01-02', '-2024-01-02', '12345-01-02', '2024-01-02T03:04:05.1234567890123', '2024-01-02T03:04:05Z',
    '2024-01-02T03:04:05z', '2024-01-02T03:04:05+05:30:00', '2024-01-02T03:04:05+0530', '2024-01-02T03:04:05+0530x',
    '2024-01-02T03:04Z', '2024-01-02T03:04+01:00', '2024-01-02T03:04:05.-1', '2024-01-02T03:04:05.5+01:00',
    '2024-13-01T00:00:00', '2024-02-30T00:00:00', '2024-01-02X', '2024-01-0', '2024-01-', '202', '2024-01-02é',
    'é024-01-02', '2024-01-02\U0001F600',
    '1', '-1', '+1', '1.', '.5', '-.5', '-1.5', '1.5e3', '1.5e-3', '1.e2', '00001', '1_0', '20000000000',
    '20000000001', '-20000000000', '-20000000001', '20000000000.5', '20000000000.0', '-20000000000.5', '253402300799',
    '253402300800', '253402300799999', '253402300800000', '-62135596800', '-62135596801', '-62167219200',
    '-62167219201', '-62135596800000', '-62167219200000', '-62167219201000', '99999999999999999999',
    '9223372036854775807', '-9223372036854775808', '1.0000005', '1.0000004', '1.9999995', '-1.0000005', '0.0000005',
    '1700000000.123456789', '1700000000.5', '1e10', '1.5.5', '1.5x', '-', '+', '.', '-0', '1 ', ' 1',
    '١', '١٢٣٤٥', '1' * 400 + '.0', '0.' + '1' * 400, '1' * 30 + '.5',
    0, -1, 1, 2, 1.0, -1.5, -0.5, 1.0000005, 1.9999995, -1.0000005, 0.0000005, 0.0000015, 0.4999999, 253402300799,
    253402300800, 253402300799999, 253402300800000, 253402300799999.9, -62135596800, -62135596801, -62167219200,
    -62167219201, -62135596800000, -62167219200000, -62167219201000, 20000000000, 20000000001, -20000000000,
    -20000000001, 2e10 + 0.5, -2e10 - 0.5, 1e15 + 0.3, 1700000000.123456789, 1700000000.1234565, -1700000000.1234565,
    1e-7, 5e-324, -5e-324, 1e19, -1e19, 9.3e18, 86399.9999999,
    b'2024-01-02T03:04:05Z', b'\xff' * 10, b'2024-01-02\xff', b'1.5',
]

BOOL_EXTRA = [3, -2, 100, 2.5, -1.0, 3.0, 0.1, -0.5, 1e18, 9.3e18, 'yes ', 'Yes', 'yEs', 'TrUe', 'FALSE', 'ı',
              'ﬁ', b'TRUE', b'y', b'2', [0], {0}]

INT_EXTRA = ['1_000_000', '-1_000', '1 000', '1,000', '٣', '１２', '+-1', '-+1', ' -1 ', '- 1', '0001.000', '1.000001',
             '1.0.0', '-1.5', '-0.0', '-.0', '1.0e1', '0.', '00.00', '99999999999999999999.0', '1' * 4300 + '.0',
             '1.' + '0' * 5000, 3.0e18, 9.223372036854775e18, -9.223372036854776e18, 1.0000000000000002, 0.9999999999999999,
             -0.5, 1e-300, 1e16 + 2, b'-1', b'1.0', b' 1_0 ', b'\xff1']

FLOAT_EXTRA = ['1e5', '1E-5', '+.5', '-.5', '+1.', '1e+05', '1.7976931348623159e308', '2.2250738585072014e-308',
               '4.9e-324', '2e-324', '0.1e1_0', '1_000_000.000_1', '1__0.0', '_1.0', '1.0_', 'in f', 'nan ', '\tinf',
               'infinityy', 'Inf', '+nan', 'NAN', 'iNfInItY', '-Infinity', '1.5 ', '　1', '1 ',
               '0' * 400 + '1', '1' + '0' * 400, '.' + '0' * 400 + '1', '123456789012345678901234567890',
               '0.1000000000000000055511151231257827021181583404541015625', b'1e3', b'inf', b'1_0']

STR_EXTRA = ['\x00', 'a' * 100, ' ', 'ﬁ', b'\xed\xa0\x80', b'\xf0\x9f\x98\x80', b'abc\x00', bytearray(b'x')]


def enum_inputs(cls) -> list:
    out = []
    for m in cls:
        out += [m, m.value, m.name, m.value.upper(), m.value + ' ', m.value.encode()]
    return out


# ---------------------------------------------------------------------------------------------
# Models
# ---------------------------------------------------------------------------------------------

def dt_now():
    return datetime.datetime(2024, 5, 6, 7, 8, 9, 123456, tzinfo=datetime.timezone.utc)


MODEL_INPUTS: dict[str, list[dict]] = {
    'DateFilter': [
        {'comparison_operator': '='},
        {'date': '2024-01-02T03:04:05Z', 'comparison_operator': '>'},
        {'date': None, 'comparison_operator': ComparisonOperator.is_null},
        {'date': 1700000000, 'comparison_operator': '<=', 'extra': 1},
        {'date': 'x'},
        {},
        {'date': [], 'comparison_operator': 'equals'},
        {'comparison_operator': None},
    ],
    'PropertyFilter': [
        {'property_name': 'age', 'property_value': 3, 'comparison_operator': '>'},
        {'property_name': 'age', 'property_value': '3', 'comparison_operator': '>'},
        {'property_name': 'age', 'property_value': 3.5, 'comparison_operator': '>'},
        {'property_name': 'age', 'property_value': True, 'comparison_operator': '='},
        {'property_name': 'age', 'property_value': None, 'comparison_operator': 'IS NULL'},
        {'property_name': 'age', 'comparison_operator': 'IS NULL'},
        {'property_name': 'age', 'property_value': [1], 'comparison_operator': '='},
        {'property_name': 'age', 'property_value': b'x', 'comparison_operator': '='},
        {'property_name': 1, 'property_value': {}, 'comparison_operator': 'bad'},
        {'property_name': 'n', 'property_value': 2**70, 'comparison_operator': '='},
        {'property_value': 2**63 - 1, 'comparison_operator': '='},
    ],
    'SearchFilters': [
        {},
        {'node_labels': ['Person', 'Org_1']},
        {'node_labels': ['Person', '1bad', 'bad-label']},
        {'node_labels': []},
        {'node_labels': None, 'edge_types': ('A', 'B')},
        {'node_labels': 'Person'},
        {'node_labels': ['ok', 3]},
        {'valid_at': [[{'date': '2024-01-02', 'comparison_operator': '>='}, {'comparison_operator': 'IS NULL'}], []]},
        {'valid_at': [[{'date': 'bad', 'comparison_operator': '>='}], [{'comparison_operator': 'nope'}, 'x']]},
        {'valid_at': [{'date': None, 'comparison_operator': '='}]},
        {'created_at': [[DateFilter(comparison_operator=ComparisonOperator.less_than,
                                    date=datetime.datetime(2024, 1, 1, tzinfo=datetime.timezone.utc))]]},
        {'property_filters': [{'property_name': 'a', 'property_value': 1.0, 'comparison_operator': '<>'}]},
        {'property_filters': [{'property_name': 'a', 'property_value': [], 'comparison_operator': '<>'}]},
        {'edge_uuids': ['u1', 'u2'], 'edge_source_node_uuids': {'s'}, 'edge_target_node_uuids': None,
         'expired_at': None, 'invalid_at': [[]]},
        {'node_labels': ['1bad'], 'edge_types': 5},
    ],
    'EntityNode': [
        {'uuid': 'u1', 'name': 'Alice', 'group_id': 'g', 'created_at': '2024-01-02T03:04:05Z'},
        {'name': 'Alice', 'group_id': 'g'},
        {'uuid': 'u1', 'name': 'Alice', 'group_id': 'g', 'labels': ['Entity', 'Person'], 'created_at': dt_now(),
         'name_embedding': [0.5, 1, '2.5', True], 'summary': 'sum', 'attributes': {'age': 3, 'tags': ['a'], 'n': None}},
        {'uuid': 'u1', 'name': 'Alice', 'group_id': 'g', 'created_at': 0, 'name_embedding': None, 'summary': None},
        {'uuid': 1, 'name': None, 'labels': ['ok', 'not ok'], 'created_at': 'yesterday', 'attributes': []},
        {'uuid': 'u1', 'name': 'A', 'group_id': 'g', 'created_at': '2024-01-02', 'labels': ['1x']},
        {'uuid': 'u1', 'name': 'A', 'group_id': 'g', 'created_at': '2024-01-02', 'name_embedding': [1, 'x', None]},
        {'uuid': 'u1', 'name': 'A', 'group_id': 'g', 'created_at': '2024-01-02', 'attributes': {1: 'a'}},
    ],
    'EpisodicNode': [
        {'uuid': 'e1', 'name': 'ep', 'group_id': 'g', 'created_at': '2024-01-02T00:00:00+00:00', 'source': 'message',
         'source_description': 'chat', 'content': 'hi', 'valid_at': '2024-01-02T00:00:00Z'},
        {'uuid': 'e1', 'name': 'ep', 'group_id': 'g', 'created_at': '2024-01-02T00:00:00+00:00',
         'source': EpisodeType.json, 'source_description': 'chat', 'content': '{}', 'valid_at': 1700000000,
         'entity_edges': ['x'], 'episode_metadata': {'k': (1, 2)}},
        {'uuid': 'e1', 'name': 'ep', 'group_id': 'g', 'created_at': '2024-01-02T00:00:00+00:00', 'source': 'Message',
         'source_description': 'chat', 'content': 'hi', 'valid_at': '2024-01-02T00:00:00Z', 'episode_metadata': None},
        {'uuid': 'e1', 'name': 'ep', 'group_id': 'g', 'source': 'fact_triple', 'content': 'hi',
         'valid_at': '2024-01-02T25:00:00Z', 'episode_metadata': 'x'},
    ],
    'NodeDuplicate': [
        {'id': 0, 'name': 'A', 'duplicate_candidate_id': -1},
        {'id': '1', 'name': 'A', 'duplicate_candidate_id': 2.0},
        {'id': 1.5, 'name': 7, 'duplicate_candidate_id': 'x'},
    ],
    'NodeResolutions': [
        {'entity_resolutions': []},
        {'entity_resolutions': [{'id': 0, 'name': 'A', 'duplicate_candidate_id': -1},
                                {'id': '1', 'name': 'B', 'duplicate_candidate_id': '3', 'extra': True}]},
        {'entity_resolutions': [{'id': 0, 'name': 'A'}, 'x', None, {'id': 'one', 'name': 'B',
                                                                     'duplicate_candidate_id': 1}]},
        {'entity_resolutions': {'id': 0}},
        {},
        {'entity_resolutions': [NodeDuplicate(id=4, name='C', duplicate_candidate_id=5)]},
        {'entity_resolutions': [{'id': 1, 'name': 'A', 'duplicate_candidate_id': 2, 3: 'x', None: 'y'}]},
        {'entity_resolutions': [{'id': 1, 'name': 'A', 1: 2}]},
    ],
    'Edge': [
        {'source_entity_name': 'A', 'target_entity_name': 'B', 'relation_type': 'KNOWS', 'fact': 'A knows B'},
        {'source_entity_name': 'A', 'target_entity_name': 'B', 'relation_type': 'KNOWS', 'fact': 'f',
         'valid_at': '2024-01-02T00:00:00Z', 'invalid_at': None, 'episode_indices': [0, '1', 2.0]},
        {'source_entity_name': 'A', 'target_entity_name': 'B', 'relation_type': 'KNOWS', 'fact': 'f',
         'valid_at': 20240102, 'episode_indices': [0.5]},
    ],
    'ExtractedEdges': [
        {'edges': []},
        {'edges': [{'source_entity_name': 'A', 'target_entity_name': 'B', 'relation_type': 'R', 'fact': 'f'}]},
        {'edges': [{'source_entity_name': 'A', 'relation_type': 'R', 'fact': 'f'},
                   {'source_entity_name': 'A', 'target_entity_name': 'B', 'relation_type': 'R', 'fact': None}]},
        {'edges': None},
    ],
    'SystemMessage': [{'content': 'c'}, {'role': 'system', 'content': 'c'}, {'role': 'user', 'content': 'c'},
                      {'role': None}, {'role': b'system', 'content': 'c'}],
    'UserMessage': [{'content': 'c'}, {'role': 'User', 'content': 1}],
    'ChatPrompt': [
        {'system': {'content': 's'}, 'user': {'content': 'u'}},
        {'system': SystemMessage(content='s'), 'user': {'role': 'user', 'content': 'u'}},
        {'system': {'role': 'user', 'content': 's'}, 'user': 'u'},
        {'system': UserMessage(content='u'), 'user': {'content': 'u'}},
    ],
    'EdgeSearchConfig': [
        {'search_methods': ['bm25', 'cosine_similarity']},
        {'search_methods': ['breadth_first_search'], 'reranker': 'mmr', 'sim_min_score': '0.5', 'mmr_lambda': 1,
         'bfs_max_depth': '3'},
        {'search_methods': ['bfs'], 'reranker': 'rrf', 'bfs_max_depth': 2.5},
        {'search_methods': [EdgeSearchMethod.bm25], 'reranker': EdgeReranker.cross_encoder},
    ],
    'NodeSearchConfig': [{'search_methods': []}, {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}],
    'SearchConfig': [
        {},
        {'edge_config': {'search_methods': ['bm25']}, 'limit': '20', 'reranker_min_score': 1},
        {'edge_config': {'search_methods': ['bm25']}, 'node_config': {'search_methods': ['bfs']}, 'limit': None},
        {'edge_config': None, 'node_config': None, 'limit': 10**20},
        {'node_config': 'x', 'reranker_min_score': 'nan'},
    ],
    'ConcurrentIngestionConfig': [
        {'window': 1},
        {'window': '4', 'lease_seconds': 90, 'write_margin_seconds': '10', 'poll_interval_seconds': 0.5,
         'index_lag_seconds': 0, 'max_apply_attempts': 1, 'enabled_for_group': None},
        {'window': 0},
        {},
        {'window': 2, 'lease_seconds': 0, 'write_margin_seconds': -1, 'poll_interval_seconds': 0,
         'index_lag_seconds': -0.5, 'max_apply_attempts': 0},
        {'window': 2, 'lease_seconds': 3, 'write_margin_seconds': 1},
        {'window': 2, 'state_store': 'not a store'},
        {'window': 2, 'enabled_for_group': 'x', 'before_commit_write': 1},
        {'window': 2, 'lease_seconds': 'nan'},
        {'window': 2, 'lease_seconds': float('inf'), 'write_margin_seconds': float('nan')},
        {'window': 2, 'lease_seconds': 60.0, 'write_margin_seconds': 19.999999},
        {'window': 2, 'lease_seconds': 60.0, 'write_margin_seconds': 20},
        {'window': True},
    ],
    'Ops': [
        {},
        {'operations': [{'op': 'add_episodes_to_edge', 'edge_uuid': 'e', 'episode_uuids': ['a']},
                        {'op': 'end_edge', 'edge_uuid': 'e', 'expired_at': '2024-01-02T00:00:00Z'},
                        {'op': 'set_edge_attributes', 'edge_uuid': 'e', 'attributes': {'k': 1}},
                        {'op': 'patch_node', 'node_uuid': 'n', 'labels': ['L']}]},
        {'operations': [{'edge_uuid': 'e'}, {'op': 'nope'}, {'op': 1}, 'x', None,
                        {'op': 'end_edge', 'edge_uuid': 'e'}, {'op': 'patch_node', 'node_uuid': 'n', 'labels': ['1']}]},
        {'operations': [{'op': 'end_edge', 'edge_uuid': 'e', 'expired_at': 0, 'only_if_open': 'yes',
                         'invalid_at': None}]},
        {'operations': [{'op': 'End_Edge', 'edge_uuid': 'e'}, {'op': None}]},
        {'operations': [{'op': 'patch_node', 'node_uuid': 'n', 7: 'x'}, {1: 'patch_node'}]},
    ],
}


def model_case(name: str, data: dict) -> dict:
    cls = MODELS[name]
    case = {'model': name, 'data': enc(data)}
    try:
        m = cls.model_validate(data)
    except ValidationError as e:
        case['errors'] = errors_of(e)
        case['text'] = cps(text_of(e))
        return case
    dump = m.model_dump()
    if contains_big(dump):
        # An int that a long cannot hold, in a field that is not Any: the port throws OverflowException (R4).
        case['big'] = True
        return case
    json_dump = m.model_dump(mode='json')
    for (model_name, field) in UNSTABLE_DEFAULTS:
        if model_name == name and field not in data:
            dump[field] = Anything()
            json_dump[field] = Anything()
    case['dump'] = enc(dump)
    case['json'] = enc(json_dump)
    return case


JSON_EXTRA = [
    {True: 1, False: 2}, {1.0: 'a', -0.0: 'b', float('nan'): 'c', float('inf'): 'd', 1e20: 'e', 2**70: 'f'},
    {datetime.date(2024, 1, 2): 1, aware(2024, 1, 2): 2, naive(2024, 1, 2, 3, 4, 5, 6): 3}, {(1, 2): 3, ('a', (2,)): 4},
    {b'ab': 1}, {None: 1, 'None': 2}, {ComparisonOperator.equals: 1, EpisodeType.json: 2},
    naive(1, 1, 1, 0, 0, 0, 5), aware(2024, 1, 2, 3, 4, 5, offset_minutes=-330), aware(9999, 12, 31, 23, 59, 59, 999999),
    datetime.datetime(2024, 1, 2, tzinfo=datetime.timezone(datetime.timedelta(seconds=-1))),
    datetime.datetime(2024, 1, 2, tzinfo=datetime.timezone(datetime.timedelta(hours=23, minutes=59, seconds=59))),
    datetime.datetime(2024, 1, 2, tzinfo=datetime.timezone(datetime.timedelta(seconds=90, microseconds=5))),
    [float('nan'), (float('inf'), {1, 2}), {'a': [datetime.date(1, 1, 1)]}], b'\xc3\xa9', b'\xed\xa0\x80',
]


def json_value_cases() -> list:
    """model_dump(mode='json') of an Any field, which Pydantic.JsonValue gives for a value of the value model."""
    model = one_field(Any)
    out = []
    for v in dedupe(COMMON + JSON_EXTRA):
        case = {'v': enc(v)}
        try:
            case['r'] = enc(model(x=v).model_dump(mode='json')['x'])
        except UnicodeDecodeError as e:
            case['e'] = type(e).__name__
            case['msg'] = cps(str(e))
        out.append(case)
    return out


def model_fields() -> dict:
    return {name: [[f, info.is_required()] for f, info in cls.model_fields.items()] for name, cls in MODELS.items()}


# ---------------------------------------------------------------------------------------------
# Wrap
# ---------------------------------------------------------------------------------------------

def wrap_cases() -> list:
    """What pydantic gives when a validator raises ValueError: the field validator of SearchFilters
    with one bad label and with three, and the model validator of ConcurrentIngestionConfig."""
    out = []
    for name, data in [('SearchFilters', {'node_labels': ['ok', '1bad']}),
                       ('SearchFilters', {'node_labels': ['a b', '-', 'é']}),
                       ('ConcurrentIngestionConfig', {'window': 1, 'lease_seconds': 3.0, 'write_margin_seconds': 1.0}),
                       ('ConcurrentIngestionConfig', {'window': 1, 'state_store': 7})]:
        try:
            MODELS[name].model_validate(data)
        except ValidationError as e:
            (er,) = e.errors()
            inner = er['ctx']['error']
            out.append({'model': name, 'data': enc(data), 'inner': cps(str(inner)), 'errors': errors_of(e),
                        'text': cps(text_of(e))})
    return out


# ---------------------------------------------------------------------------------------------
# Field types
# ---------------------------------------------------------------------------------------------

def dedupe(values: list) -> list:
    out, seen = [], set()
    for v in values:
        key = (type(v).__name__, repr(v))
        if key not in seen:
            seen.add(key)
            out.append(v)
    return out


def field_cases(tp, inputs, **field) -> list:
    model = one_field(tp, **field)
    keeps_big = 'Any' in str(tp)
    return [field_case(model, v, keeps_big) for v in dedupe(inputs) if not isinstance(v, bytearray)]


def build_values() -> dict:
    common = COMMON
    cases = {
        'str': field_cases(str, common + STR_EXTRA),
        'int': field_cases(int, common + INT_EXTRA),
        'float': field_cases(float, common + FLOAT_EXTRA),
        'bool': field_cases(bool, common + BOOL_EXTRA),
        'datetime': field_cases(datetime.datetime, common + DATETIME_EXTRA),
        'any': field_cases(Any, common),
        'enum': field_cases(ComparisonOperator, common + enum_inputs(ComparisonOperator)),
        'enum_episode_type': field_cases(EpisodeType, common + enum_inputs(EpisodeType)),
        'literal': field_cases(Literal['system'], common + ['system', b'system', 'SYSTEM']),
        'str_int_float_none': field_cases(str | int | float | None, common + INT_EXTRA + FLOAT_EXTRA),
        'optional_str': field_cases(str | None, common),
        'optional_int': field_cases(int | None, common),
        'optional_float': field_cases(float | None, common),
        'optional_bool': field_cases(bool | None, common),
        'optional_datetime': field_cases(datetime.datetime | None, common),
        'list_str': field_cases(list[str], common + [['a', 1], ['a', None, b'b'], ('a',), {'x': 1}, ['é', '\ud800']]),
        'list_int': field_cases(list[int], common + [[1, '2', 3.0, True], [1.5, 'x', None], [2**70], [[]]]),
        'list_float': field_cases(list[float], common + [[1, '2.5', True, float('nan')], ['x', [], None]]),
        'list_bool': field_cases(list[bool], common + [[1, 0, 'yes', 'off'], [2, 'maybe']]),
        'optional_list_str': field_cases(list[str] | None, common + [['a', 'b'], ['a', 2]]),
        'list_enum': field_cases(list[EdgeSearchMethod], common + [['bm25', 'breadth_first_search'],
                                                                  [EdgeSearchMethod.bfs, 'bfs', 'x']]),
        'dict_any': field_cases(dict[str, Any], common + [{'a': 1, 'b': [1, (2,)], 'c': {'d': None}},
                                                          {'a': 1, 2: 'b', None: 3}, {(1,): 1}]),
        'optional_dict_any': field_cases(dict[str, Any] | None, common),
        'dict_str': field_cases(dict[str, str], common + [{'a': 'x', 'b': 1, 'c': None}, {1: 'x'}]),
        'dict_float': field_cases(dict[str, float], common + [{'a': 1, 'b': '2.5', 'c': 'x'}]),
        'dict_list_str': field_cases(dict[str, list[str]], common + [{'a': ['x'], 'b': [1], 'c': 'x', 'd': ('y',)}]),
        'callable': field_cases(Callable[[str], bool] | None, [None, 'x', 1, [], b'']),
        'ge_int': field_cases(int, [0, 1, 2, -1, '0', '1', True, False, 1.0, 0.0, LONG_MIN, LONG_MAX], ge=1),
        'gt_float': field_cases(float, [0, 0.0, -0.0, 5e-324, -5e-324, 1, '0', float('nan'), float('inf'),
                                        float('-inf'), -1.5, 60.0, True, False], gt=0),
        'ge_float': field_cases(float, [0, 0.0, -0.0, -5e-324, 5e-324, -1, '-0', float('nan'), float('inf'),
                                        float('-inf'), 300.0, False], ge=0),
        'max_length': field_cases(str | None, [None, '', 'abcde', 'abcdef', '\U0001F600' * 5, '\U0001F600' * 6,
                                               'é' * 3, '\ud800' * 6, b'abcdef', 1], default=None, max_length=5),
    }
    models = [model_case(name, data) for name, inputs in MODEL_INPUTS.items() for data in inputs]
    return {
        'python': sys.version.split()[0],
        'pydantic': pydantic.VERSION,
        'pydantic_core': pydantic_core.__version__,
        'cases': cases,
        'models': models,
        'fields': model_fields(),
        'json_value': json_value_cases(),
        'wrap': wrap_cases(),
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=False) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_pydantic_values: up to date')
        return 0
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
