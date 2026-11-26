"""Records what real Python gives for the value model helpers of PyCompat (RULES.md R6, R11, R14, R18).

One file comes out of a run:

- tests/Graphiti.Net.Property.Tests/python-values.valuemodel.json: inputs and the Python result of each
  one, for repr, str, format, bool(), len, dict and set behavior, key equality, tuple unpacking,
  copy.deepcopy, enum values, and the typed reads.

    .upstream/graphiti/.venv/bin/python port/tools/generate_valuemodel_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_valuemodel_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.

A value is written as a tagged JSON object, because JSON cannot tell a list from a tuple or hold a
float bit pattern or a lone surrogate:

    null, true, false      None, True, False
    {"i": "-7b"}           int, as hexadecimal text with a sign, because str() of an int has a limit
    {"f": bits}            float, the 64 bits as a signed integer
    {"s": "text"}          str. A text with a surrogate code point is a list of code points instead,
                           because JSON text cannot hold a lone surrogate. Each other text in the file
                           follows the same rule.
    {"l": [...]}           list
    {"t": [...]}           tuple
    {"d": [[k, v], ...]}   dict
    {"set": [...], "frozen": b}   set or frozenset, items in Python's iteration order
    {"date": [y, m, d]}    datetime.date
    {"dt": [y, m, d, h, mi, s, us, offset]}  datetime.datetime. The offset is in microseconds, or null
                           for a naive value.
    {"b": [bytes]}         bytes
    {"enum": "Color.red"}  a member of a test enum. The C# test enum has the same class name (R2).
    {"node": "u"}, {"edge": "u"}  an object of a test class with the __eq__ of upstream Node and Edge

A list, dict, tuple, nan, node, or edge that occurs more than once in one case gets "id": n at its first
occurrence, and each later occurrence is {"ref": n}. The test then builds the same object graph.
A set is written in Python's iteration order. R14 says that the order of a set is not a contract,
so no test compares the order of a set with Python. A set with more than one item appears only where
the test builds it in that same order, which makes repr comparable.
"""
from __future__ import annotations

import copy
import datetime
import enum
import json
import math
import os
import random
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.valuemodel.json'
SEED = 20261126

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_valuemodel_values: needs Python 3.12, found {sys.version.split()[0]}')

# The iteration order of a set of strings depends on the hash seed of the process. A fixed seed makes
# the file the same on each run. The order itself is not an oracle (R14).
if os.environ.get('PYTHONHASHSEED') != '0':
    os.execve(sys.executable, [sys.executable, *sys.argv], {**os.environ, 'PYTHONHASHSEED': '0'})

LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1


class Color(enum.Enum):
    red = 'red'
    green = 2
    blue = 'bl\u00e9'


class EpisodeType(enum.Enum):
    """A copy of graphiti_core.nodes.EpisodeType."""
    message = 'message'
    json = 'json'
    text = 'text'


class ComparisonOperator(enum.Enum):
    """A copy of graphiti_core.search.search_filters.ComparisonOperator."""
    equals = '='
    not_equals = '<>'
    greater_than = '>'
    less_than = '<'
    greater_than_equal = '>='
    less_than_equal = '<='
    is_null = 'IS NULL'
    is_not_null = 'IS NOT NULL'


class Priority(enum.Enum):
    """An enum with int values."""
    low = 1
    high = 2
    huge = 2**62


ENUMS = {'Color': Color, 'EpisodeType': EpisodeType, 'ComparisonOperator': ComparisonOperator, 'Priority': Priority}


class Node:
    """The equality of graphiti_core.nodes.Node (nodes.py:149)."""

    def __init__(self, uuid: str):
        self.uuid = uuid

    def __hash__(self):
        return hash(self.uuid)

    def __eq__(self, other):
        if isinstance(other, Node):
            return self.uuid == other.uuid
        return False


class Edge:
    """The equality of graphiti_core.edges.Edge (edges.py:96), with the isinstance(other, Node) defect U12."""

    def __init__(self, uuid: str):
        self.uuid = uuid

    def __hash__(self):
        return hash(self.uuid)

    def __eq__(self, other):
        if isinstance(other, Node):
            return self.uuid == other.uuid
        return False


def cps(s: str) -> str | list[int]:
    """A text for the file: the text itself, or its code points if it holds a surrogate."""
    if any(0xD800 <= ord(c) <= 0xDFFF for c in s):
        return [ord(c) for c in s]
    return s


def hex_int(n: int) -> str:
    return format(n, 'x')


def bits(x: float) -> int:
    return struct.unpack('<q', struct.pack('<d', x))[0]


def from_bits(b: int) -> float:
    return struct.unpack('<d', struct.pack('<q', b))[0]


def has_joinable_surrogates(s: str) -> bool:
    return any(0xD800 <= ord(a) <= 0xDBFF and 0xDC00 <= ord(b) <= 0xDFFF for a, b in zip(s, s[1:]))


# ---------------------------------------------------------------------------------------------
# Encoding of values
# ---------------------------------------------------------------------------------------------

def _shared(v) -> bool:
    return isinstance(v, (list, dict, tuple, Node, Edge)) or (isinstance(v, float) and math.isnan(v))


class Encoder:
    """Encodes the values of one case. Objects that occur twice share an id across the whole case."""

    def __init__(self):
        self.counts: dict[int, int] = {}
        self.ids: dict[int, int] = {}
        self.keep: list = []

    def count(self, v) -> None:
        if _shared(v):
            self.counts[id(v)] = self.counts.get(id(v), 0) + 1
            self.keep.append(v)
            if self.counts[id(v)] > 1:
                return
        if isinstance(v, (list, tuple, set, frozenset)):
            for x in v:
                self.count(x)
        elif isinstance(v, dict):
            for k, x in v.items():
                self.count(k)
                self.count(x)

    def enc(self, v):
        if _shared(v) and self.counts.get(id(v), 0) > 1:
            if id(v) in self.ids:
                return {'ref': self.ids[id(v)]}
            self.ids[id(v)] = len(self.ids)
            out = self._enc(v)
            out['id'] = self.ids[id(v)]
            return out
        return self._enc(v)

    def _enc(self, v):
        if v is None or isinstance(v, bool):
            return v
        if isinstance(v, enum.Enum):
            return {'enum': f'{type(v).__name__}.{v.name}'}
        if isinstance(v, int):
            return {'i': hex_int(v)}
        if isinstance(v, float):
            return {'f': bits(v)}
        if isinstance(v, str):
            assert not has_joinable_surrogates(v), repr(v)
            return {'s': cps(v)}
        if isinstance(v, list):
            return {'l': [self.enc(x) for x in v]}
        if isinstance(v, tuple):
            return {'t': [self.enc(x) for x in v]}
        if isinstance(v, dict):
            return {'d': [[self.enc(k), self.enc(x)] for k, x in v.items()]}
        if isinstance(v, (set, frozenset)):
            return {'set': [self.enc(x) for x in v], 'frozen': isinstance(v, frozenset)}
        if isinstance(v, datetime.datetime):
            offset = v.utcoffset()
            micros = None if offset is None else offset // datetime.timedelta(microseconds=1)
            return {'dt': [v.year, v.month, v.day, v.hour, v.minute, v.second, v.microsecond, micros]}
        if isinstance(v, datetime.date):
            return {'date': [v.year, v.month, v.day]}
        if isinstance(v, bytes):
            return {'b': list(v)}
        if isinstance(v, Node):
            return {'node': v.uuid}
        if isinstance(v, Edge):
            return {'edge': v.uuid}
        raise TypeError(type(v))


def encode_case(**values) -> dict:
    """Encodes several values of one case with one id table."""
    e = Encoder()
    for v in values.values():
        e.count(v)
    return {name: e.enc(v) for name, v in values.items()}


def err(e: BaseException) -> dict:
    return {'e': type(e).__name__, 'msg': cps(str(e))}


def text_or_error(thunk) -> dict:
    try:
        return {'r': cps(thunk())}
    except (ValueError, TypeError, OverflowError, KeyError, AttributeError) as e:
        return err(e)


# ---------------------------------------------------------------------------------------------
# Inputs
# ---------------------------------------------------------------------------------------------

STRINGS = ['', 'a', "it's", 'say "hi"', 'both \' and "', 'é', 'é', '\U0001F600', '\ud800', '\udfff',
           'tab\tnew\nret\r\\', '\x00\x07\x1f\x7f\x85\xa0', 'Ω', ' ', 'x' * 50, 'Ab\U0001F600́' * 9]

FLOATS = [0.0, -0.0, 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, -2.5, 0.05, 0.25, 1234.5, 1234567.25, 999999.95, 123.456,
          1e16, 1e15, 9999999999999998.0, 1e-4, 1e-5, 0.00011, 1.5e-5, 1e-7, 5e-324, -5e-324,
          2.2250738585072014e-308, 1.7976931348623157e308, 1e22, 1e23, 1e100, 1e300, 2.0**53, 2.0**63, 2.0**64,
          1 / 3, 2 / 3, 0.3, 100.0, 123456789012345678.0, math.inf, -math.inf]

INTS = [0, 1, -1, 5, -5, 42, 1234567, -1234567, 2**53 + 1, LONG_MAX, LONG_MIN, LONG_MAX + 1, LONG_MIN - 1,
        10**30, -(10**30), 2**1024 - 2**970, 2**1024 - 2**971, 10**4300 - 1, -(10**4300 - 1), 10**4300]


UTC = datetime.timezone.utc
PLUS1 = datetime.timezone(datetime.timedelta(hours=1))
MINUS530 = datetime.timezone(-datetime.timedelta(hours=5, minutes=30))
DATETIMES = [datetime.datetime(2025, 1, 3, tzinfo=UTC), datetime.datetime(2025, 1, 3, 1, 0, tzinfo=PLUS1),
             datetime.datetime(2025, 1, 3), datetime.datetime(2024, 2, 29, 23, 59, 59, 999999, tzinfo=MINUS530),
             datetime.datetime(1, 1, 1), datetime.datetime(9999, 12, 31, 23, 59, 59, 1, tzinfo=UTC),
             datetime.datetime(2025, 1, 3, 0, 0, 7)]


def nan(sign: int = 1) -> float:
    return math.copysign(math.nan, sign)


def pool() -> list:
    """Values of each type of the closed set, with nested and shared values."""
    shared = [1, 'x']
    cyclic = [1]
    cyclic.append(cyclic)
    cyclic_dict: dict = {'a': 1}
    cyclic_dict['self'] = cyclic_dict
    inner: list = []
    tuple_in_cycle = (2, inner)
    inner.append(tuple_in_cycle)
    holder = [inner]
    return [
        None, True, False,
        *INTS,
        *FLOATS, nan(), nan(-1),
        *STRINGS,
        [], [1], [1, 'a', None, 2.5, True], [[1, [2]], {'k': [3]}], ["it's", 'say "hi"'], [(1,), ()],
        (), (1,), (1, 2), ((1,),), ('a', None, (2.0,)), (1, [2, 3]), ('\U0001F600',),
        {}, {'a': 1}, {'a': [1, {'b': None}], 'é': 'x', '': ()},
        {1: 'a', 'b': 2}, {(1, 2): None, None: 1.5, 2.5: True}, {True: 'x'}, {datetime.date(2024, 1, 5): 1},
        {b'x': 1}, {Color.red: 1}, {frozenset({3}): (4,)}, {(1, frozenset({2})): 'v', (1, 2): [3]},
        set(), {1}, {'a'}, {(1, 2)}, {None}, {1, 2, 3}, frozenset(), frozenset({1}), frozenset({'a', 'b'}),
        datetime.date(2024, 1, 5), datetime.date(1, 1, 1), datetime.date(9999, 12, 31),
        b'', b'abc', b"it's", b'say "hi"', b'\'"', b'\x00\xff\n\t\r\\\x7f\x80',
        Color.red, Color.green, Color.blue, EpisodeType.message,
        *DATETIMES, [DATETIMES[0], DATETIMES[2]], (DATETIMES[1],), {'valid_at': DATETIMES[3]},
        {DATETIMES[0]: 'a', DATETIMES[2]: 'b'},
        [shared, shared], {'a': shared, 'b': [shared]}, (shared, shared),
        cyclic, cyclic_dict, holder,
        [Color.blue, {EpisodeType.json: [datetime.date(2020, 2, 29), b'\x01']}],
    ]


def random_value(rng: random.Random, depth: int = 0):
    kinds = ['none', 'bool', 'int', 'float', 'str', 'bytes', 'date', 'enum']
    if depth < 3:
        kinds += ['list', 'tuple', 'dict', 'pydict', 'set'] * 2
    kind = rng.choice(kinds)
    if kind == 'none':
        return None
    if kind == 'bool':
        return rng.random() < 0.5
    if kind == 'int':
        return rng.choice([rng.randint(-1000, 1000), rng.randint(LONG_MIN, LONG_MAX), rng.choice(INTS[:13])])
    if kind == 'float':
        return rng.choice([rng.uniform(-1e6, 1e6), rng.choice(FLOATS), from_bits(rng.getrandbits(63))])
    if kind == 'str':
        return rng.choice(STRINGS[:-2])
    if kind == 'bytes':
        return bytes(rng.getrandbits(8) for _ in range(rng.randint(0, 6)))
    if kind == 'date':
        return datetime.date(rng.randint(1, 9999), rng.randint(1, 12), rng.randint(1, 28))
    if kind == 'enum':
        return rng.choice(list(Color) + list(EpisodeType))
    n = rng.randint(0, 4)
    if kind == 'list':
        return [random_value(rng, depth + 1) for _ in range(n)]
    if kind == 'tuple':
        return tuple(random_value(rng, depth + 1) for _ in range(n))
    if kind == 'dict':
        return {rng.choice(STRINGS[:-2]): random_value(rng, depth + 1) for _ in range(n)}
    if kind == 'pydict':
        return {rng.choice([rng.randint(-3, 3), rng.choice(STRINGS[:5]), None, (1, 'a'), 2.5]): random_value(rng, depth + 1) for _ in range(n)}
    return rng.choice([set(), {rng.randint(-5, 5)}, {rng.choice(STRINGS[:8])}, frozenset({rng.randint(0, 3)})])


def random_doubles(rng: random.Random) -> list[float]:
    out = []
    for _ in range(3000):
        x = from_bits(rng.getrandbits(64) - 2**63)
        if math.isfinite(x):
            out.append(x)
    out += [rng.uniform(-1e6, 1e6) for _ in range(1000)]
    out += [float(rng.randint(-2**60, 2**60)) for _ in range(500)]
    out += [rng.randint(-10**8, 10**8) / 10**rng.randint(0, 12) for _ in range(1000)]
    out += [rng.random() * 10.0**rng.randint(-30, 30) for _ in range(1000)]
    for e in range(-330, 309):
        out += [10.0**e if e > -324 else 5e-324, 2.0**(e / 2) if abs(e) < 2000 else 1.0]
    return out


# ---------------------------------------------------------------------------------------------
# Cases
# ---------------------------------------------------------------------------------------------

FORMAT_SPECS_UPSTREAM = ['.0f', '.1f', '.30s', '<45', '>8', '>10', '>12', '>12,', '>10,.1f']
FORMAT_SPECS_MORE = ['', 's', 'd', 'f', 'F', '.2f', '.3f', '.10f', '.40f', ',', '_', ',d', '_d', ',.2f', '^9', '*^9', '*<7',
                     '=+10', '+', ' ', '-', '+.1f', ' .0f', '08.2f', '010', '05', '0', '-<6', '\U0001F600>4', 'é^5',
                     '.0s', '.3s', '.2d', ',s', ',,', ',_', '_,', '.', '>12,d', '10.3', 'xx', '=10', '+s', '#s', '<+5']


def keys_pool(nan_a: float, nan_b: float) -> list:
    return [None, True, False, 0, 1, -1, 2, 1.0, 0.0, -0.0, 2.0, 1.5, 2**53, float(2**53), 2**53 + 1, 2**64,
            float(2**64), LONG_MIN, 10**30, 'a', 'A', '', 'é', 'é', '\U0001F600', '\ud800',
            (), (1,), (1.0,), (True,), (1, 'a'), (1, (2, 3)), (None,), nan_a, nan_b, (nan_a,), math.inf, -math.inf,
            datetime.date(2024, 1, 5), b'a', b'', Color.red, Color.green, EpisodeType.message,
            frozenset(), frozenset({1}), frozenset({1.0}), [1], {}, {1}, (1, [2]),
            DATETIMES[0], DATETIMES[1], DATETIMES[2], datetime.datetime(2025, 1, 3, 0, 0), DATETIMES[6]]


def build_values() -> dict:
    rng = random.Random(SEED)
    cases: dict[str, list] = {}

    def add(name: str, case: dict) -> None:
        cases.setdefault(name, []).append(case)

    format_errors: set = set()

    def add_format(v, spec: str) -> None:
        if spec and isinstance(v, datetime.date):
            return  # date.__format__ is strftime. Upstream formats no date with a spec.
        result = text_or_error(lambda: format(v, spec))
        if 'e' in result:
            # One case for each distinct type, spec, and message.
            key = (type(v).__name__, spec, str(result['msg']))
            if key in format_errors:
                return
            format_errors.add(key)
        elif unbuilt(v, spec):
            add('format_unbuilt', {**encode_case(v=v), 'spec': cps(spec)})
            return
        add('format', {**encode_case(v=v), 'spec': cps(spec), **result})

    values = pool() + [random_value(rng) for _ in range(400)]

    # repr, str, bool(), len, and the typed reads.
    for v in values:
        add('repr', {**encode_case(v=v), **text_or_error(lambda: repr(v))})
        add('str', {**encode_case(v=v), **text_or_error(lambda: str(v))})
        add('truthy', {**encode_case(v=v), 'r': bool(v)})
        try:
            add('len', {**encode_case(v=v), 'r': len(v)})
        except TypeError as e:
            add('len', {**encode_case(v=v), **err(e)})
        add('as', {**encode_case(v=v), **typed_reads(v)})

    # repr of floats: the boundaries, then several thousand random doubles.
    for x in FLOATS + [nan(), nan(-1)] + random_doubles(rng):
        case = {'x': bits(x), 'r': cps(repr(x))}
        if str(x) != repr(x):
            case['s'] = cps(str(x))
        add('float_repr', case)

    # format(value, spec) for each spec that upstream uses, and more specs for the grammar.
    format_values = ([None, True, False] + INTS[:10] + [10**4300] + FLOATS + [nan(), nan(-1), 10**400]
                     + STRINGS + ['x' * 31, 'y' * 30, '\U0001F600' * 31, '\U0001F600' * 20]
                     + [[1], {'a': 1}, (1,), {1: 2}, {1}, frozenset(), datetime.date(2024, 1, 5), b'ab',
                        Color.red, Color.green, EpisodeType.message, *DATETIMES]
                     + [rng.uniform(-1e7, 1e7) for _ in range(60)] + [rng.randint(-10**8, 10**8) / 100 for _ in range(60)]
                     + [from_bits(rng.getrandbits(63)) for _ in range(30)])
    for spec in FORMAT_SPECS_UPSTREAM + FORMAT_SPECS_MORE:
        for v in format_values:
            if spec in FORMAT_SPECS_MORE and spec not in ('', 's', 'd', 'f', '.2f', ',', '>12,d') and rng.random() < 0.6 and isinstance(v, float):
                continue
            if spec and isinstance(v, datetime.date):
                continue  # date.__format__ is strftime. Upstream formats no date with a spec.
            if spec in ('10.3', '.3') and isinstance(v, float):
                continue  # The general format of a float with a precision and no type. Upstream does not use it.
            add_format(v, spec)

    # Random specs of the full grammar, on one value of each kind. Python's errors must come out the same.
    # A spec that Python accepts with an option that Py.Format does not build goes to format_unbuilt.
    grammar_values = ['abc', '', '\u00e9\U0001F600', 0, 5, -1234567, True, 1.5, -0.0, 1234.5678, nan(), math.inf,
                      None, [1], Color.red, 10**30]
    for spec in random_specs(rng, 700) + ['z', 'z5', '+c', '#c', 'c', '#d', '.3000000000f', '.2147483648f',
                                          '.99999999999999999999', '99999999999999999999', '.9223372036854775807',
                                          '.9223372036854775808', 'z.1f', ',e', '010,', '010,d', '.3', '%', 'n']:
        for v in grammar_values:
            add_format(v, spec)

    # Exact .Nf rounding on values that are ties in binary, and on random values.
    for p in range(0, 8):
        for k in range(-40, 41):
            x = k / 2**rng.randint(1, 8)
            add_format(x, f'.{p}f')

    # Key equality: (a is b or a == b), as a dict, a set, and `in` compare.
    nan_a, nan_b = math.nan, float('nan')
    keys = keys_pool(nan_a, nan_b)
    eq_values = keys + [[1, 2], [1.0, 2], (1, 2), (1.0, 2), {'a': 1}, {'a': 1.0}, {'a': 1, 'b': 2}, {'b': 2, 'a': 1},
                        {1: 'x'}, {1.0: 'x'}, {True: 'x'}, set(), {1}, {1.0}, frozenset({1}), [nan_a], [nan_b],
                        {'k': nan_a}, b'1', Node('u1'), Edge('u1'),
                        {2}, {1, 2}, {1, 3}, frozenset({2}), frozenset({1, 3}), [{1}], [{2}], {'k': {1}}, {'k': {2}},
                        [1, 2], b'\x01\x02', [frozenset({1})]]
    for a in eq_values:
        for b in eq_values:
            case = encode_case(a=a, b=b)
            case['r'] = a is b or a == b
            add('equal', case)
    for k in eq_values:
        try:
            hash(k)
            add('hashable', {**encode_case(v=k), 'r': True})
        except TypeError as e:
            add('hashable', {**encode_case(v=k), **err(e)})

    # Sets of one item type each. The test builds PySet<long>, PySet<double>, PySet<string>, or PySet<bool>.
    typed_sets = [({1}, {1.0}), ({1, 2}, {1.0, 2.0}), ({1}, {2.0}), ({'a'}, {'a'}), ({'a'}, {'b'}), ({1}, {'1'}),
                  ({True}, {1.0}), ({True}, {1}), (frozenset({1.5}), {1.5}), ({2**53 + 1}, {float(2**53)}),
                  ({2**53}, {float(2**53)}), (set(), set()), (set(), {1.0}), ({1, 2}, {2.0, 3.0}), ({0}, {-0.0})]
    for a, b in typed_sets:
        add('typed_set_equal', {**encode_case(a=a, b=b), 'r': a == b})
        add('typed_set_equal', {**encode_case(a=b, b=a), 'r': b == a})

    # dict.get on a dict[str, int]: the value or None (text_utils.py:98).
    for k in ['a', 'b', 'c']:
        add('int_dict_get', {'k': k, 'r': {'a': 1, 'b': 0}.get(k)})

    # dict behavior: random operation sequences on a dict with keys of any type.
    for n in range(260):
        add('dict_ops', dict_ops(rng, keys, n))
    for n in range(60):
        add('str_dict_ops', dict_ops(rng, ['a', 'b', '', 'é', 'é', '\U0001F600', '\ud800', 'A'], n))

    # set behavior.
    # A set probe of a set is turned into a frozenset by CPython's set methods. PySet does not do that,
    # and no upstream code passes a set to a set method, so the mutable set is left out of the keys.
    set_keys = [k for k in keys if type(k) is not set]
    for n in range(260):
        add('set_ops', set_ops(rng, set_keys, n))
    for method in ('add', 'remove', 'discard'):
        try:
            getattr(frozenset({1}), method)(1)
        except AttributeError as e:
            add('frozen_mutation', {'method': method, **err(e)})

    # Identity first, then __eq__ of the stored key: Node and Edge as in upstream.
    objects = [Node('u1'), Node('u1'), Node('u2'), Edge('u1'), Edge('u1'), Edge('u2')]
    for _ in range(400):
        items = [rng.choice(objects) for _ in range(rng.randint(0, 5))]
        probes = list(objects)
        s = set(items)
        d = dict.fromkeys(items)
        case = encode_case(items=items, probes=probes)
        case['len'] = len(s)
        case['dict_len'] = len(d)
        case['in'] = [p in s for p in probes]
        case['in_dict'] = [p in d for p in probes]
        add('node_keys', case)

    # Tuple unpacking into two names (U14).
    for v in [[], [1], [1, 2], [1, 2, 3], [1, 2, 3, 4], (), ('a',), ('a', None), (1, 2, 3), [[1], (2,)]]:
        try:
            a, b = v
            add('unpack2', {**encode_case(v=v, a=a, b=b), 'ok': True})
        except ValueError as e:
            add('unpack2', {**encode_case(v=v), **err(e)})

    # copy.deepcopy: the copy prints the same, and the identity of each container in it.
    for v in values:
        if isinstance(v, (list, tuple, dict, set, frozenset, bytes)):
            add('deepcopy', deepcopy_case(v))

    # Enum members: .value, repr, str.
    for cls in ENUMS.values():
        for m in cls:
            add('enum', {**encode_case(v=m, value=m.value), 'repr': cps(repr(m)), 'str': cps(str(m))})
        for value in [m.value for m in cls] + [2.0, True, 1, 'x', 'red', '=', [1], None, 2**62 + 0.0]:
            try:
                add('enum_from_value', {'cls': name_of(cls), **encode_case(value=value, r=cls(value))})
            except ValueError as e:
                add('enum_from_value', {'cls': name_of(cls), **encode_case(value=value), **err(e)})

    # The list helpers: in, index, count, remove, with identity first.
    nan_c = float('nan')
    list_items = [1, 1.0, True, 2, 'a', None, nan_c, (1, 2), [1], Node('u1'), Node('u1'), Edge('u1'), Edge('u2'), 2**64]
    for _ in range(300):
        xs = [rng.choice(list_items) for _ in range(rng.randint(0, 6))]
        x = rng.choice(list_items + [float('nan'), 3, 'b'])
        case = encode_case(xs=xs, x=x)
        case['in'] = x in xs
        case['count'] = xs.count(x)
        try:
            case['index'] = xs.index(x)
        except ValueError as e:
            case['index_error'] = err(e) if not isinstance(x, (Node, Edge)) else {'e': 'ValueError'}
        after = list(xs)
        try:
            after.remove(x)
            # The position of the removed item: the list after remove() is xs without that position.
            case['removed'] = next(j for j in range(len(xs)) if xs[:j] + xs[j + 1:] == after and all(
                a is b for a, b in zip(xs[:j] + xs[j + 1:], after)))
        except ValueError as e:
            case['remove_error'] = err(e)
        add('list_ops', case)

    # list.sort with a key and reverse. The result is the order of the original positions.
    for n in range(500):
        keys_ = sort_keys(rng)
        for reverse in (False, True):
            order = list(range(len(keys_)))
            try:
                order.sort(key=lambda i: keys_[i], reverse=reverse)
                add('sort', {**encode_case(keys=keys_), 'reverse': reverse, 'r': order})
            except TypeError as e:
                add('sort', {**encode_case(keys=keys_), 'reverse': reverse, **err(e)})

    return {'seed': SEED, 'cases': cases}


def name_of(cls) -> str:
    return cls.__name__


def random_specs(rng: random.Random, n: int) -> list[str]:
    """Specs from the whole grammar: [[fill]align][sign][z][#][0][width][grouping][.precision][type]."""
    out = []
    for _ in range(n):
        spec = ''
        if rng.random() < 0.4:
            spec += rng.choice(['', '', '*', '0', ' ', '\u00e9']) + rng.choice('<>=^')
        spec += rng.choice(['', '', '', '+', '-', ' '])
        spec += rng.choice(['', '', '', '', 'z'])
        spec += rng.choice(['', '', '', '', '#'])
        spec += rng.choice(['', '', '', '0'])
        spec += rng.choice(['', '', '1', '7', '12'])
        spec += rng.choice(['', '', '', ',', '_', ',,', ',_'])
        spec += rng.choice(['', '', '', '.', '.0', '.2', '.10'])
        spec += rng.choice(['', '', 's', 'd', 'f', 'F', 'e', 'g', '%', 'n', 'c', 'x', 'b', 'y', 'Z'])
        out.append(spec)
    return list(dict.fromkeys(out))


def unbuilt(v, spec: str) -> bool:
    """True for a spec that Python accepts with an option that Py.Format does not build."""
    if not spec or isinstance(v, (str, enum.Enum)):
        return False
    body = spec[2:] if len(spec) >= 2 and spec[1] in '<>=^' else spec[1:] if spec[:1] in '<>=^' else spec
    kind = body[-1:] if body[-1:].isalpha() or body[-1:] == '%' else ''
    if kind in ('e', 'E', 'g', 'G', '%', 'n', 'c', 'b', 'o', 'x', 'X'):
        return True
    if '#' in body or 'z' in body:
        return True
    if isinstance(v, float) and kind == '' and '.' in body:
        return True
    # CPython puts separators into the padding when the fill is 0 and the align is '='. That happens for
    # the flag 0 with no align, and for an explicit fill 0 with '='.
    has_fill = len(spec) >= 2 and spec[1] in '<>=^'
    align = spec[1] if has_fill else spec[0] if spec[:1] in '<>=^' else None
    zero_flag = not has_fill and body.lstrip('+- ').startswith('0')
    zero_pad = (has_fill and spec[0] == '0' and align == '=') or (zero_flag and align in (None, '='))
    return zero_pad and (',' in body or '_' in body)


def sort_keys(rng: random.Random) -> list:
    """Keys of the shapes that upstream sorts by: numbers, text, tuples of two, with ties and errors."""
    n = rng.choice([0, 1, 2, 3, 5, 8, 12, 20, 63, 64, 70])
    shape = rng.choice(['int', 'float', 'num', 'str', 'tuple', 'bool_tuple', 'date', 'bytes', 'list', 'error', 'nan',
                        'datetime', 'naive', 'dt_tuple', 'dt_mixed', 'dt_none'])
    if n > 63 and shape in ('error', 'nan', 'dt_mixed', 'dt_none'):
        shape = 'num'  # The order of comparisons above 63 items is not CPython's (see Py.SortInPlace).

    def one():
        if shape == 'int':
            return rng.randint(-3, 3)
        if shape == 'float':
            return rng.choice([0.5, -0.0, 0.0, 1.5, 2.0, -1.25])
        if shape == 'num':
            return rng.choice([1, 1.0, True, 2, 2.5, -1, 0, False, 2**64, float(2**64), 2**53 + 1, float(2**53)])
        if shape == 'str':
            return rng.choice(['a', 'B', '', '\u00e9', '\U0001F600', '\uffff', 'ab', '\ud800'])
        if shape == 'tuple':
            return (rng.choice([1.0, 2.0, 2]), rng.choice(['x', 'y', 'z']))
        if shape == 'bool_tuple':
            return (rng.random() < 0.5, rng.randint(0, 2))
        if shape == 'date':
            return datetime.date(2024, rng.randint(1, 2), rng.randint(1, 3))
        if shape == 'bytes':
            return rng.choice([b'', b'a', b'ab', b'\xff', b'b'])
        if shape == 'list':
            return rng.choice([[1], [1, 2], [], [0, 5], [1.0]])
        if shape == 'nan':
            return rng.choice([1.0, float('nan'), 0.5, 2.0])
        aware = datetime.datetime(2025, 1, rng.randint(1, 3), rng.randint(0, 1), tzinfo=rng.choice([UTC, PLUS1, MINUS530]))
        if shape == 'datetime':
            return aware  # sorted(episodes, key=lambda e: e.valid_at), graphiti.py:2204
        if shape == 'naive':
            return datetime.datetime(2025, 1, rng.randint(1, 3), rng.randint(0, 1))
        if shape == 'dt_tuple':
            return (aware, rng.choice(['u1', 'u2', 'u3']))  # (created_at, uuid), dedup_helpers.py:286
        if shape == 'dt_mixed':
            return rng.choice([aware, datetime.datetime(2025, 1, 2)])
        if shape == 'dt_none':
            return rng.choice([aware, None, 1.5])
        return rng.choice([1, 'a', None, (1, None), (1, 2), 2.5, Color.red, [1], (1,)])

    return [one() for _ in range(n)]


def typed_reads(v) -> dict:
    """What a typed read of v must give. The oracle is Python's isinstance, and float() for a float read."""
    out: dict = {}
    out['bool'] = isinstance(v, bool)
    if isinstance(v, int):
        n = int(v)
        out['int'] = {'r': hex_int(n)} if LONG_MIN <= n <= LONG_MAX else {'big': hex_int(n)}
    else:
        out['int'] = None
    if isinstance(v, (int, float)):
        try:
            out['float'] = {'r': bits(float(v))}
        except OverflowError as e:
            out['float'] = err(e)
    else:
        out['float'] = None
    out['str'] = isinstance(v, str)
    out['list'] = isinstance(v, list)
    out['tuple'] = isinstance(v, tuple)
    out['dict'] = isinstance(v, dict) and all(isinstance(k, str) for k in v)
    out['pydict'] = isinstance(v, dict) and not all(isinstance(k, str) for k in v)
    # A Python datetime is a date. Py.AsDate takes a date only, because a DateOnly would drop the time.
    out['date'] = isinstance(v, datetime.date) and not isinstance(v, datetime.datetime)
    out['datetime'] = isinstance(v, datetime.datetime)
    out['bytes'] = isinstance(v, bytes)
    out['enum'] = isinstance(v, Color)
    return out


def result_of(thunk) -> dict:
    try:
        return {'r': cps(repr(thunk()))}
    except (KeyError, TypeError) as e:
        return err(e)


def dict_ops(rng: random.Random, keys: list, n: int) -> dict:
    d: dict = {}
    ops = []
    results = []
    for i in range(rng.randint(1, 14)):
        k = rng.choice(keys)
        v = rng.choice([i, f'v{i}', None, i + 0.5])
        op = rng.choice(['set', 'set', 'set', 'get', 'get_default', 'getitem', 'del', 'in', 'setdefault', 'setdefault_none'])
        if op == 'set':
            results.append(result_of(lambda: d.__setitem__(k, v)))
        elif op == 'get':
            results.append(result_of(lambda: d.get(k)))
        elif op == 'get_default':
            results.append(result_of(lambda: d.get(k, v)))
        elif op == 'getitem':
            results.append(result_of(lambda: d[k]))
        elif op == 'del':
            results.append(result_of(lambda: d.__delitem__(k)))
        elif op == 'in':
            results.append(result_of(lambda: k in d))
        elif op == 'setdefault':
            results.append(result_of(lambda: d.setdefault(k, v)))
        else:
            results.append(result_of(lambda: d.setdefault(k)))
        ops.append([op, k, v])
    case = encode_case(ops=ops)
    case['results'] = results
    case['repr'] = cps(repr(d))
    case['len'] = len(d)
    return case


def set_ops(rng: random.Random, keys: list, n: int) -> dict:
    s: set = set()
    ops = []
    results = []
    for _ in range(rng.randint(1, 14)):
        k = rng.choice(keys)
        op = rng.choice(['add', 'add', 'add', 'in', 'discard', 'remove'])
        if op == 'add':
            results.append(result_of(lambda: s.add(k)))
        elif op == 'in':
            results.append(result_of(lambda: k in s))
        elif op == 'discard':
            results.append(result_of(lambda: s.discard(k)))
        else:
            results.append(result_of(lambda: s.remove(k)))
        ops.append([op, k])
    case = encode_case(ops=ops)
    case['results'] = results
    # The content of the set, as the sorted repr text of its items. The order of a set is not a contract.
    case['items'] = [cps(r) for r in sorted(repr(x) for x in s)]
    case['len'] = len(s)
    return case


def paths(v, prefix=(), stack=()) -> list[tuple]:
    """The paths to each container inside v in depth-first order. A shared object has one path for each
    place where it occurs. A path stops where it would enter an object a second time (a cycle)."""
    if id(v) in stack:
        return []
    stack = stack + (id(v),)
    out = [prefix]
    children = list(v.values()) if isinstance(v, dict) else list(v) if isinstance(v, (list, tuple)) else []
    for i, x in enumerate(children):
        if isinstance(x, (list, tuple, dict, set, frozenset, bytes)):
            out += paths(x, prefix + (i,), stack)
    if isinstance(v, dict):
        # A key is step -(i + 1): deepcopy copies keys too, which a frozenset key shows.
        for i, k in enumerate(v):
            if isinstance(k, (tuple, frozenset, bytes)):
                out += paths(k, prefix + (-(i + 1),), stack)
    return out


def at(v, path):
    for i in path:
        if isinstance(v, dict):
            v = list(v)[-i - 1] if i < 0 else list(v.values())[i]
        else:
            v = v[i]
    return v


def deepcopy_case(v) -> dict:
    c = copy.deepcopy(v)
    ps = paths(v)
    same_as_original = [at(c, p) is at(v, p) for p in ps]
    alias = []
    for p in ps:
        target = at(c, p)
        alias.append(next(j for j, q in enumerate(ps) if at(c, q) is target))
    case = encode_case(v=v)
    case.update({'repr': cps(repr(c)), 'paths': [list(p) for p in ps], 'same': same_as_original, 'alias': alias})
    return case


def main() -> int:
    check = '--check' in sys.argv[1:]
    # A text with a surrogate is a list of code points, so the rest can be plain UTF-8.
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=False) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_valuemodel_values: up to date')
        return 0
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
