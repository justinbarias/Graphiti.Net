"""Records what real Python gives for PyJson and PyAst (RULES.md R11, Part 2).

One file comes out of a run:

- tests/Graphiti.Net.Property.Tests/python-values.pyjson.json: inputs and the Python result of each one,
  for json.dumps, json.loads, and ast.literal_eval.

    .upstream/graphiti/.venv/bin/python port/tools/generate_pyjson_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_pyjson_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.

A value is written as a tagged JSON object, as in generate_valuemodel_values.py: null, true, false,
{"i": hex}, {"f": bits}, {"s": text}, {"l": [...]}, {"t": [...]}, {"d": [[k, v], ...]}, {"set": [...],
"frozen": b}, {"date": [...]}, {"dt": [...]}, {"b": [...]}, {"enum": "Color.red"}. A text with a surrogate
code point is a list of code points, because JSON text cannot hold a lone surrogate. Extra tags:

    {"strenum": "Mode.fast"}      a member of a test enum that derives from str (the C# enum has [PyStrEnum])
    {"srep": [unit, n]}           the text unit repeated n times (a long text)
    {"deep": [kind, n, leaf]}     n nested lists ("l"), tuples ("t"), or dicts with the key "a" ("d")
    {"chain": [n, leaf]}          n objects of a test class that are not JSON types, each with the next inside

A text that is the input of loads or literal_eval is a plain text, a list of code points, or
{"parts": [[text, n], ...]}: each text n times, joined.

A container, nan, or chain that occurs more than once in one case gets "id": n at its first occurrence, and
each later occurrence is {"ref": n}.

A result is {"r": ...} or {"e": class name, "msg": text}. A long text result is {"rlen": code points,
"rsha": sha256 of the UTF-8 bytes, with a lone surrogate written as three bytes} in place of "r". No case holds a high surrogate directly before a low
surrogate: a .NET string would join them.
"""
from __future__ import annotations

import ast
import datetime
import enum
import hashlib
import json
import math
import os
import random
import struct
import sys
import warnings
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.pyjson.json'
SEED = int(os.environ.get('PYJSON_SEED', '20261127'))  # another seed gives other random cases, for a search for defects

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_pyjson_values: needs Python 3.12, found {sys.version.split()[0]}')

# The iteration order of a set of strings depends on the hash seed of the process.
if os.environ.get('PYTHONHASHSEED') != '0':
    os.execve(sys.executable, [sys.executable, *sys.argv], {**os.environ, 'PYTHONHASHSEED': '0'})

warnings.simplefilter('ignore')

LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1
BIG_TEXT = 4000
SCALE = int(os.environ.get('PYJSON_SCALE', '1'))  # a larger scale gives more random cases, for a search for defects


class Color(enum.Enum):
    red = 'red'
    green = 2
    blue = 'bl\u00e9'


class Priority(enum.Enum):
    low = 1
    high = 2
    huge = 2**62


class Mode(str, enum.Enum):
    """A str enum: json.dumps writes it as the text."""
    fast = 'fast'
    slow = 's\u00e9'


class Chain:
    """An object that json cannot write. The default function walks to the next one."""

    def __init__(self, next_):
        self.next = next_


class Rep(str):
    """A long text made of one unit repeated."""
    unit: str
    n: int


def rep(unit: str, n: int) -> Rep:
    r = Rep(unit * n)
    r.unit = unit
    r.n = n
    return r


def cps(s: str) -> str | list[int]:
    if any(0xD800 <= ord(c) <= 0xDFFF for c in s):
        return [ord(c) for c in s]
    return s


def hex_int(n: int) -> str:
    return format(n, 'x')


def bits(x: float) -> int:
    return struct.unpack('<q', struct.pack('<d', x))[0]


def from_bits(b: int) -> float:
    return struct.unpack('<d', struct.pack('<Q', b & 0xFFFFFFFFFFFFFFFF))[0]


def has_joinable_surrogates(s: str) -> bool:
    return any(0xD800 <= ord(a) <= 0xDBFF and 0xDC00 <= ord(b) <= 0xDFFF for a, b in zip(s, s[1:]))


def nan(sign: int = 1) -> float:
    return math.copysign(math.nan, sign)


# ---------------------------------------------------------------------------------------------
# Encoding of values
# ---------------------------------------------------------------------------------------------

def _shared(v) -> bool:
    return isinstance(v, (list, dict, tuple, Chain)) or (isinstance(v, float) and math.isnan(v))


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
        if isinstance(v, Mode):
            return {'strenum': f'{type(v).__name__}.{v.name}'}
        if isinstance(v, enum.Enum):
            return {'enum': f'{type(v).__name__}.{v.name}'}
        if isinstance(v, int):
            return {'i': hex_int(v)}
        if isinstance(v, float):
            return {'f': bits(v)}
        if isinstance(v, Rep):
            return {'srep': [cps(v.unit), v.n]}
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
        if isinstance(v, Chain):
            n = 0
            x = v
            while isinstance(x, Chain):
                n += 1
                x = x.next
            return {'chain': [n, self.enc(x)]}
        raise TypeError(type(v))


def encode(v) -> dict:
    e = Encoder()
    e.count(v)
    return e.enc(v)


def text(s: str):
    """A text for the file."""
    assert not has_joinable_surrogates(s), repr(s)
    return cps(s)


def err(e: BaseException) -> dict:
    return {'e': type(e).__name__, 'msg': cps(str(e))}


def text_result(s: str) -> dict:
    if len(s) > BIG_TEXT:
        return {'rlen': len(s), 'rsha': hashlib.sha256(s.encode('utf-8', 'surrogatepass')).hexdigest()}
    return {'r': text(s)}


# ---------------------------------------------------------------------------------------------
# json.dumps
# ---------------------------------------------------------------------------------------------

def _not_serializable(o):
    raise TypeError(f'Object of type {type(o).__name__} is not JSON serializable')


def _isoformat(o):
    if isinstance(o, datetime.date):
        return o.isoformat()
    return _not_serializable(o)


def _value(o):
    if isinstance(o, enum.Enum):
        return o.value
    return _not_serializable(o)


def _tolist(o):
    if isinstance(o, (set, frozenset, bytes)):
        return list(o)
    return _not_serializable(o)


def _boom(o):
    raise ValueError('boom')


DEFAULTS = {
    'str': str,
    'repr': repr,
    'isoformat': _isoformat,
    'value': _value,
    'tolist': _tolist,
    'wrap': lambda o: {'repr': repr(o)},
    'self': lambda o: o,
    'boom': _boom,
    'none': lambda o: None,
    'chain': lambda o: o.next if isinstance(o, Chain) else _not_serializable(o),
    'listchain': lambda o: [o.next] if isinstance(o, Chain) else _not_serializable(o),
    'pair': lambda o: (repr(o), 1),
}

# (ensure_ascii, indent, sort_keys, compact, allow_nan)
COMBOS = [
    (True, None, False, False, True),    # json.dumps(x)
    (False, None, False, False, True),   # to_prompt_json(x) and json.dumps(x, ensure_ascii=False)
    (True, None, True, False, True),     # json.dumps(x, sort_keys=True)
    (False, None, True, False, True),
    (True, 2, False, False, True),
    (False, 2, False, False, True),
    (False, 0, False, False, True),
    (True, 4, True, False, True),
    (False, None, False, True, False),   # the httpx body (RULES.md R19)
    (True, None, False, True, True),
    (False, 3, True, False, False),
    (True, 1, False, True, True),
]


def py_dumps(v, ia, ind, sk, cp, nan_ok, default):
    kw = dict(ensure_ascii=ia, indent=ind, sort_keys=sk, allow_nan=nan_ok)
    if cp:
        kw['separators'] = (',', ':')
    if default:
        kw['default'] = DEFAULTS[default]
    return json.dumps(v, **kw)


def dumps_case(v, combo, default=None, pool_index=None) -> dict:
    """One case. A value of the pool is stored one time in "pool" and a case names it with "vi"."""
    ia, ind, sk, cp, nan_ok = combo
    case = {('vi' if pool_index is not None else 'v'): pool_index if pool_index is not None else encode(v), 'ia': ia, 'ind': ind, 'sk': sk, 'cp': cp, 'nan': nan_ok, 'd': default}
    try:
        case.update(text_result(py_dumps(v, ia, ind, sk, cp, nan_ok, default)))
    except (TypeError, ValueError, RecursionError) as e:
        case.update(err(e))
    return case


UTC = datetime.timezone.utc
PLUS1 = datetime.timezone(datetime.timedelta(hours=1))
DATETIMES = [datetime.datetime(2025, 1, 3, tzinfo=UTC), datetime.datetime(2025, 1, 3, 1, 0, tzinfo=PLUS1),
             datetime.datetime(2025, 1, 3), datetime.datetime(2024, 2, 29, 23, 59, 59, 999999, tzinfo=UTC)]

FLOATS = [0.0, -0.0, 1.0, -1.0, 0.1, 0.5, 1.5, 2.5, 100.0, 1234.5, 123.456, 1e15, 1e16, 9999999999999998.0, 1e-4, 1e-5,
          0.00011, 1.5e-5, 1e-7, 5e-324, -5e-324, 2.2250738585072014e-308, 1.7976931348623157e308, 1e22, 1e23, 1e100,
          2.0**53, 2.0**63, 2.0**64, 1 / 3, 2 / 3, 0.3, 123456789012345678.0, 1.0000000000000002, 0.30000000000000004,
          math.inf, -math.inf, nan(), nan(-1)]
INTS = [0, 1, -1, 7, 42, -42, 255, 65535, 2**31, -(2**31), 2**53 + 1, LONG_MAX, LONG_MIN, LONG_MAX + 1, LONG_MIN - 1,
        10**30, -(10**30), 2**1024, 10**4300 - 1, -(10**4300 - 1), 10**4300, 10**4300 + 5]

SPECIAL_STRINGS = (
    [chr(i) for i in range(0x20)]
    + ['', ' ', '"', '\\', '/', '\x7f', '\x80', '\x9f', '\xa0', '\xad', 'é', 'é', '\u0100', '\u2028', '\u2029', '\ufeff',
       '\ud7ff', '\ue000', '\uffff', '\ufffe', '\U00010000', '\U0001F600', '\U0010FFFF', 'a\u0301', '\u200d',
       '\U0001F468\u200d\U0001F469\u200d\U0001F467', '\ud800', '\udbff', '\udc00', '\udfff', '\ud800x', 'x\udc00',
       '\udc00\ud800', '"\\/\b\f\n\r\t', 'tab\there', 'line\nbreak', 'quote"back\\slash', '<script>&', "it's", '日本語', '한국어',
       'Ünïcödé', '\u0000a\u001fb', '\U0001F600\U0001F601', 'x' * 100, 'a\x00b', '\n' * 5]
)


def string_pool() -> list[str]:
    return [s for s in SPECIAL_STRINGS if not has_joinable_surrogates(s)]


def pool() -> list:
    shared = [1, 'x']
    cyclic = [1]
    cyclic.append(cyclic)
    cyclic_dict: dict = {'a': 1}
    cyclic_dict['self'] = cyclic_dict
    inner: list = []
    tuple_in_cycle = (2, inner)
    inner.append(tuple_in_cycle)
    dict_in_list: list = []
    dict_in_list.append({'k': dict_in_list})
    big = {f'k{i:04d}': i for i in range(300)}
    unicode_keys = {chr(0x1F600): 1, '\uffff': 2, '\ue000': 3, 'z': 4, 'a': 5, '\u00e9': 6, 'E': 7, '\U00010000': 8, '': 9}
    return [
        None, True, False,
        *INTS,
        *FLOATS,
        *string_pool(),
        [], [1], [1, 'a', None, 2.5, True, False], [[1, [2]], {'k': [3]}], ['it\'s', 'say "hi"'], [(1,), ()], [[]], [{}], [[], {}],
        [None] * 40, list(range(300)), [1.5] * 30,
        (), (1,), (1, 2), ((1,),), ('a', None, (2.0,)), (1, [2, 3]), ('\U0001F600',),
        {}, {'a': 1}, {'b': 1, 'a': 2}, {'a': [1, {'b': None}], 'é': 'x', '': ()}, {'z': {'y': {'x': []}}}, {'a': {}, 'b': []},
        {'\n': 1, '"': 2, '\\': 3, '\x00': 4, '\U0001F600': 5, '\ud800': 6}, unicode_keys, big,
        {'k': 1.0, 'j': -0.0, 'i': 1e22, 'h': 5e-324, 'g': float('inf')}, {'n': float('nan')},
        # keys that are not strings
        {1: 'a'}, {1: 'a', 'b': 2}, {2: 'x', 1: 'y'}, {1.5: 'a'}, {1e22: 1}, {1e-7: 1}, {-0.0: 1}, {True: 'x'}, {False: 'y', 2: 3},
        {None: 1}, {2**70: 1}, {-(2**70): 1}, {float('inf'): 1}, {float('-inf'): 1}, {float('nan'): 1}, {10**4300: 1},
        {1: 'a', 2.5: 'b', None: 'c', 7: 'd'},
        {(1, 2): 1}, {Color.red: 1}, {Mode.fast: 1}, {Mode.slow: 1, 'a': 2}, {datetime.date(2024, 1, 5): 1}, {DATETIMES[0]: 1},
        {b'x': 1}, {frozenset(): 1}, {frozenset({1}): 1}, {Priority.low: 1},
        {'a': 1, (1, 2): 2}, {1: 1, 'b': {(1,): 2}},
        # sort keys across types
        {3: 'c', 1: 'a', 2: 'b'}, {2.5: 'a', 1: 'b', 3: 'c'}, {True: 1, 2: 2, 0.5: 3}, {'b': 1, 1: 2}, {1: 1, 'b': 2},
        {None: 1, 'a': 2}, {'a': 1, None: 2}, {1: 1, None: 2, 'c': 3}, {'a': 1, 'b': 2, 3: 3}, {3: 1, 'a': 2, 'b': 3},
        {2: 1, 1: 2, 'x': 3, 'y': 4}, {(1,): 1, (2,): 2}, {Color.red: 1, Color.green: 2}, {Mode.slow: 1, Mode.fast: 2},
        {'b': {'d': 1, 'c': 2}, 'a': [{'z': 1, 'y': 2}]}, {1: {2: 3}}, {'a': 1, 'B': 2, 'b': 3, 'A': 4},
        {-1: 1, 10: 2, 2: 3, -10: 4}, {1.5: 1, 1: 2, 2**70: 3, -(2**70): 4}, {'': 1, ' ': 2, 'a': 3, '\x00': 4},
        # types that json cannot write
        {1, 2}, set(), frozenset(), {'a'}, b'', b'abc', b'\xff', datetime.date(2024, 1, 5), DATETIMES[0], DATETIMES[2],
        Color.red, Color.blue, Priority.high, Color.green, Mode.fast, Mode.slow,
        [Color.red], {'a': Color.green}, [Mode.fast], {'m': Mode.slow}, {'s': {1}}, [b'x', DATETIMES[1]],
        [datetime.date(2020, 2, 29), {1, 2}], {'k': (1, {2})}, [Priority.low, Priority.huge],
        # shared and circular
        [shared, shared], {'a': shared, 'b': [shared]}, (shared, shared), cyclic, cyclic_dict, inner, dict_in_list,
        [nan(), nan()], [float('inf')] * 3,
        # long and wide
        rep('a', 200000), rep('\U0001F600', 30000), rep('é\n"', 40000), [rep('x', 120000), 1],
        {'big': rep('y', 150000)},
    ]


def random_string(rng: random.Random) -> str:
    kind = rng.random()
    if kind < 0.3:
        return rng.choice(string_pool())
    pool_chars = (['a', 'b', ' ', '"', '\\', '/', '\n', '\t', '\x00', '\x1f', '\x7f', '\x80', 'é', '日', '\u2028', '\uffff',
                   '\U0001F600', '\U00010000', '\u0301', '\u200d', '\ud800', '\udc00', '\ude00', '\udbff'])
    while True:
        s = ''.join(rng.choice(pool_chars) for _ in range(rng.randint(0, 12)))
        if not has_joinable_surrogates(s):
            return s


def random_float(rng: random.Random) -> float:
    k = rng.random()
    if k < 0.3:
        return rng.choice(FLOATS)
    if k < 0.6:
        return rng.uniform(-1e6, 1e6)
    if k < 0.8:
        return round(rng.uniform(-1000, 1000), rng.randint(0, 6))
    return from_bits(rng.getrandbits(64))


def random_int(rng: random.Random) -> int:
    k = rng.random()
    if k < 0.5:
        return rng.randint(-1000, 1000)
    if k < 0.8:
        return rng.randint(LONG_MIN, LONG_MAX)
    return rng.choice(INTS[:20]) if k < 0.9 else rng.randint(-(10**40), 10**40)


def random_value(rng: random.Random, depth: int = 0, exotic: float = 0.0, keys: str = 'str'):
    kinds = ['none', 'bool', 'int', 'float', 'str']
    if depth < 4:
        kinds += ['list', 'tuple', 'dict'] * 2
    if exotic and rng.random() < exotic:
        kinds += ['set', 'bytes', 'date', 'enum', 'datetime', 'strenum']
    kind = rng.choice(kinds)
    if kind == 'none':
        return None
    if kind == 'bool':
        return rng.random() < 0.5
    if kind == 'int':
        return random_int(rng)
    if kind == 'float':
        return random_float(rng)
    if kind == 'str':
        return random_string(rng)
    if kind == 'list':
        return [random_value(rng, depth + 1, exotic, keys) for _ in range(rng.randint(0, 5))]
    if kind == 'tuple':
        return tuple(random_value(rng, depth + 1, exotic, keys) for _ in range(rng.randint(0, 4)))
    if kind == 'dict':
        d = {}
        for _ in range(rng.randint(0, 5)):
            k = random_key(rng, keys)
            d[k] = random_value(rng, depth + 1, exotic, keys)
        return d
    if kind == 'set':
        return set(rng.sample(range(100), rng.randint(0, 3)))
    if kind == 'bytes':
        return bytes(rng.getrandbits(8) for _ in range(rng.randint(0, 4)))
    if kind == 'date':
        return datetime.date(rng.randint(1, 9999), rng.randint(1, 12), rng.randint(1, 28))
    if kind == 'datetime':
        return rng.choice(DATETIMES)
    if kind == 'strenum':
        return rng.choice(list(Mode))
    return rng.choice(list(Color) + list(Priority))


def random_key(rng: random.Random, keys: str):
    if keys == 'str' or rng.random() < 0.6:
        return random_string(rng)
    if keys == 'sortable':
        return rng.choice([rng.randint(-5, 5), rng.randint(-5, 5) + 0.5, rng.randint(LONG_MIN, LONG_MAX), random_float(rng)])
    return rng.choice([rng.randint(-100, 100), random_float(rng), True, False, None, 2**70, (1,), Color.red])


POOL: list = []


def make_dumps() -> list[dict]:
    rng = random.Random(SEED)
    cases = []
    pool_values = pool()
    for i, v in enumerate(pool_values):
        POOL.append(encode(v))
        for combo in COMBOS:
            cases.append(dumps_case(v, combo, pool_index=i))
    # indent: a negative number writes new lines with no spaces, and a wide one writes many
    for v in ([1, [2, {'a': []}]], {'a': {'b': [1, 2]}, 'c': ()}):
        for ind in (-1, -5, 1, 8):
            cases.append(dumps_case(v, (True, ind, False, False, True)))
    # default functions on values that json cannot write
    exotic = [{1, 2}, set(), frozenset({3}), b'abc', datetime.date(2024, 1, 5), DATETIMES[0], DATETIMES[2], Color.red, Priority.high, Mode.fast,
              [Color.red, {1}], {'a': b'x', 'b': [DATETIMES[1]]}, {'s': {'k': {1, 2}}}, (Color.green, b''), [{Color.red: 1}], 5,
              {'a': 1, 'b': {1}}, [[{7}]]]
    for name in DEFAULTS:
        for v in exotic:
            for combo in (COMBOS[0], COMBOS[1], COMBOS[3], COMBOS[5], COMBOS[8]):
                cases.append(dumps_case(v, combo, name))
    # random values
    for i in range(3600 * SCALE):
        v = random_value(rng, exotic=0.0)
        combo = rng.choice(COMBOS)
        cases.append(dumps_case(v, combo))
    for i in range(1200 * SCALE):
        v = random_value(rng, exotic=0.5)
        combo = rng.choice(COMBOS)
        default = rng.choice([None, None, 'str', 'repr', 'isoformat', 'value', 'tolist', 'wrap', 'pair', 'none'])
        cases.append(dumps_case(v, combo, default))
    for i in range(800 * SCALE):
        v = random_value(rng, exotic=0.0, keys='any')
        cases.append(dumps_case(v, rng.choice(COMBOS)))
    for i in range(600 * SCALE):
        v = random_value(rng, exotic=0.0, keys='sortable')
        cases.append(dumps_case(v, rng.choice([c for c in COMBOS if c[2]])))
    # dicts of 2 to 63 keys: Python sorts them by a run and then by insertion, and a mixed key shows the pair that it compares
    for n in (2, 3, 5, 10, 31, 32, 33, 60, 63):
        for kind in range(6):
            d = {}
            while len(d) < n:
                d[random_string(rng) if kind % 2 == 0 else rng.randint(-50, 50)] = len(d)
            if kind >= 4:
                d[[None, 'zz', 2.5, (1,)][kind % 4]] = 0
            cases.append(dumps_case(d, COMBOS[2]))
    # a large dict, sorted by code point
    wide = {}
    while len(wide) < 2000:
        wide[random_string(rng) or 'k'] = len(wide)
    cases.append(dumps_case(wide, COMBOS[2]))
    cases.append(dumps_case(wide, COMBOS[5]))
    return cases


def deep_obj(kind: str, n: int, leaf):
    x = leaf
    for _ in range(n):
        x = [x] if kind == 'l' else (x,) if kind == 't' else {'a': x}
    return x


def make_deep_dumps() -> list[dict]:
    cases = []
    for kind in ('l', 't', 'd'):
        for n in (100, 997, 5000, 9996, 9997, 9998, 9999, 20000):
            for leaf in (1, []):
                v = deep_obj(kind, n, leaf)
                combo = COMBOS[0]
                case = {'v': {'deep': [kind, n, encode(leaf)]}, 'ia': combo[0], 'ind': combo[1], 'sk': combo[2], 'cp': combo[3],
                        'nan': combo[4], 'd': None}
                try:
                    case.update(text_result(py_dumps(v, *combo, None)))
                except RecursionError as e:
                    case.update(err(e))
                cases.append(case)
    # sort_keys and the ensure_ascii=False path at the limit
    for n in (9996, 9997, 9998):
        v = deep_obj('d', n, 1)
        for combo in (COMBOS[3], COMBOS[8]):
            case = {'v': {'deep': ['d', n, encode(1)]}, 'ia': combo[0], 'ind': combo[1], 'sk': combo[2], 'cp': combo[3], 'nan': combo[4], 'd': None}
            try:
                case.update(text_result(py_dumps(v, *combo, None)))
            except RecursionError as e:
                case.update(err(e))
            cases.append(case)
    # the indent path recurses in Python code. A small depth works.
    for n in (10, 100, 500):
        v = deep_obj('l', n, 1)
        combo = COMBOS[4]
        case = {'v': {'deep': ['l', n, encode(1)]}, 'ia': combo[0], 'ind': combo[1], 'sk': combo[2], 'cp': combo[3], 'nan': combo[4], 'd': None}
        case.update(text_result(py_dumps(v, *combo, None)))
        cases.append(case)
    # a chain of objects that default unwraps, at the limit
    for name in ('chain', 'listchain'):
        for n in (10, 4997, 4998, 4999, 5000, 9995, 9996, 9997, 9998):
            x = 1
            for _ in range(n):
                x = Chain(x)
            combo = COMBOS[0]
            case = {'v': {'chain': [n, encode(1)]}, 'ia': combo[0], 'ind': combo[1], 'sk': combo[2], 'cp': combo[3], 'nan': combo[4], 'd': name}
            try:
                case.update(text_result(py_dumps(x, *combo, name)))
            except RecursionError as e:
                case.update(err(e))
            cases.append(case)
    return cases


# ---------------------------------------------------------------------------------------------
# json.loads
# ---------------------------------------------------------------------------------------------

def loads_case(t, py_text: str, parts=None) -> dict:
    case = {'t': t}
    try:
        v = json.loads(py_text)
    except json.JSONDecodeError as e:
        case.update({'e': 'JSONDecodeError', 'msg': cps(str(e))})
        return case
    except (ValueError, RecursionError) as e:
        case.update(err(e))
        return case
    if isinstance(v, str) and len(v) > BIG_TEXT:
        case.update({'rlen': len(v), 'rsha': hashlib.sha256(v.encode('utf-8', 'surrogatepass')).hexdigest()})
        return case
    seq, leaf = chain_shape(v)
    if len(seq) <= 100 and result_has_joinable(v):
        return None
    if len(seq) > 100:
        # A deep result: the test builds the same chain. 'l' is a list of one item and 'd' is {"a": item}.
        case['rchain'] = [seq, encode(leaf)]
        return case
    case['r'] = encode(v)
    return case


def result_has_joinable(v) -> bool:
    """True if a text in the result has a high surrogate before a low one. A .NET string would join them."""
    if isinstance(v, str):
        return has_joinable_surrogates(v)
    if isinstance(v, (list, tuple, set, frozenset)):
        return any(result_has_joinable(x) for x in v)
    if isinstance(v, dict):
        return any(result_has_joinable(k) or result_has_joinable(x) for k, x in v.items())
    return False


def chain_shape(v) -> tuple[str, object]:
    seq = []
    while True:
        if isinstance(v, list) and len(v) == 1:
            seq.append('l')
            v = v[0]
        elif isinstance(v, dict) and len(v) == 1 and 'a' in v:
            seq.append('d')
            v = v['a']
        else:
            return ''.join(seq), v


def loads_text_case(s: str) -> dict | None:
    if has_joinable_surrogates(s):
        return None
    return loads_case(text(s), s)


def number_texts() -> list[str]:
    out = ['0', '-0', '1', '-1', '9', '10', '-10', '0.0', '-0.0', '0.5', '-0.5', '1.0', '1.5', '1e5', '1E5', '1e+5', '1e-5', '1E-5', '1.5e5', '1.5E+5',
           '1.5e-5', '0e0', '0e5', '0.0e0', '-0e0', '123456789', '1234567890123456789', str(LONG_MAX), str(LONG_MIN), str(LONG_MAX + 1), str(LONG_MIN - 1),
           str(2**64), str(-(2**64)), str(10**30), str(2**100), '9' * 400, '1' * 4300, '1' * 4301, '-' + '1' * 4300, '-' + '1' * 4301, '9' * 5000,
           '1e308', '1e309', '-1e309', '1e999', '1e-999', '-1e-999', '1e400', '1.7976931348623157e308', '1.7976931348623159e308', '5e-324', '2.5e-324', '4.9e-324',
           '2.2250738585072014e-308', '2.2250738585072011e-308', '0.1', '0.30000000000000004', '3.141592653589793', '123456789012345678901234567890.123',
           '0.000001', '0.0000001', '1e16', '1e15', '1e22', '1e23', '9007199254740993', '9007199254740993.0', '1' + '0' * 400 + '.5', '0.' + '0' * 400 + '1',
           '1e' + '9' * 30, '1e-' + '9' * 30, '1.' + '1' * 1000, '-1.5e300', '1E+0', '00', '01', '-01', '0123', '-00', '1.', '.5', '-.5', '+1', '1e', '1e+', '1e-', '1.e5',
           '1.5e', '0x10', '1_000', '- 1', '--1', '-', '+', '1 2', '1,', 'Infinity', '-Infinity', '+Infinity', 'NaN', '-NaN', 'nan', 'inf', 'Inf', 'INFINITY',
           'Infinit', 'Infinityx', '-Infinit', 'NaNa', 'Na', 'NA', '1e5e5', '1.5.5', '1..5', '1e5.5', '0.e1', '-e1', '\u0661', '1\u0661', '١٢٣']
    return out


def literal_texts() -> list[str]:
    return ['true', 'false', 'null', 'True', 'False', 'None', 'TRUE', 'NULL', 'tru', 'fals', 'nul', 'truee', 'nulll', 'falsee', 't', 'f', 'n', 'N', 'I', 'u', 'undefined',
            'trUe', ' true', 'true ', '"a" "b"', "'a'", '{"a": \'b\'}', "{'a': 1}", '{a: 1}', '[1, 2,]', '[,]', '[,1]', '{,}', '{"a":1,}', '{"a":}', '{"a"}', '{"a" 1}', '{"a":1 "b":2}',
            '{1: 2}', '{"a": 1, "a": 2}', '{"a": 1, "b": 2, "a": 3}', '{"": 1, "": 2}', '[1 2]', '[1, 2', '[', ']', '{', '}', '{"a"', '{"a":', '{"a":1', '{"a":1,', '{"a":1,"b"', '[[', '[[]', '[]]',
            '{}}', '[}', '{]', '[1}', '{"a":1]', '', ' ', '\n', '\t', '\r', ' \t\r\n ', '\x0b', '\x0c', '\xa0', '\u2003', '\ufeff', '\ufeff[]', ' \ufeff', '"', '"abc', '"abc\\', '"\\', '"\\"',
            '[1]x', '[1] x', '[1][2]', '1 1', 'null null', '{} {}', '[1]\n\n', '\n[1]', '[\n1\n,\n2\n]', '[ 1 , 2 ]', '{ "a" : 1 , "b" : 2 }', '\t[\t1\t]\t', '\r\n[\r\n]\r\n', '[1,\x0b2]',
            '[1,\x0c2]', '[1,\xa02]', '/* c */ 1', '// c\n1', '# c\n1', '[1, /* c */ 2]', '"\\u0041"', '"\\u00e9"', '"\\u00E9"', '"\\uD83D\\uDE00"', '"\\ud83d\\ude00"', '"\\uD83D"', '"\\uDE00"',
            '"\\uDE00\\uD83D"', '"\\uD83D\\uD83D"', '"\\uD83D\\u0041"', '"\\uD83D\\n"', '"\\uD83Dx"', '"\\uD83D\\uDE0"', '"\\uD83D\\uDE0x"', '"\\uD83D\\uDE00x"', '"\\uD83D\\uDE00\\uD83D\\uDE00"',
            '"\\uD83D\\uZZZZ"', '"\\uD83D\\u"', '"\\u"', '"\\u1"', '"\\u12"', '"\\u123"', '"\\u1234"', '"\\u12345"', '"\\uXYZW"', '"\\u00g0"', '"\\U0041"', '"\\x41"', '"\\a"', '"\\v"', '"\\0"',
            '"\\/"', '"\\b\\f\\n\\r\\t\\\\\\""', '"\\\'"', '"\\ "', '"a\\', '"\\u0000"', '"\\u001f"', '"\\u007f"', '"\\uffff"', '"\\ufffe"', '"\\uFEFF"', '"\\u2028"',
            '"\t"', '"\n"', '"\r"', '"\x00"', '"\x1f"', '"\x7f"', '"\x80"', '"\u2028"', '"\U0001F600"', '"\ud800"', '"\udc00"', '"\ud800x"', '"x\udc00"', '"\udc00\ud800"',
            '"é"', '"\u0301"', '"日本語"', '"a\\\nb"', '"a\nb"', '{"\\u00e9": 1}', '{"\\ud800": 1}', '{"\\u0041": 1, "A": 2}', '{"a": {"b": {"c": [1, {"d": null}]}}}', '[[[[[[[[[[1]]]]]]]]]]',
            '[1, [2, [3, [4]]]]', '{"a": [], "b": {}}', '[{}]', '[[], []]', '{"a":1}{"b":2}', '{"a":1},', '[1],', '[1];', '[1])', '(1)', '<1>', '`1`', '[NaN]', '[Infinity, -Infinity, NaN]',
            '{"a": NaN}', '{NaN: 1}', '[-Infinity]', '[- Infinity]', '[+Infinity]', '[nan]', '[NaN, NaN]', '[1e999, -1e999]', '[1,\n2,\n3', '{\n"a":\n1,\n"b"\n}', '{\n"a":\n1,\n"b"', '[\n\n\n',
            '\n\n\nx', '\r\n\r\nx', '[\r1,\r2\r', 'é', '日', '\U0001F600', '"\\u0041\\u0042\\u0043"', '"' + 'a' * 100 + '"', '"' + '\\n' * 100 + '"', '[' + '1,' * 1000 + '1]', '{' + ','.join(f'"k{i}":{i}' for i in range(500)) + '}',
            '"\\ud83d\\ude00" ', ' "\\ud83d\\ude00"', '{"a": "\\ud83d\\ude00", "\\ud83d\\ude00": 1}', '"\ud83d\\ude00"', '"\\ud83d\ude00"' if False else '"\\ud83d"', '{"a":1,"a":2,"a":3}', '{"b":1,"a":2,"b":3}']


def make_loads() -> list[dict]:
    rng = random.Random(SEED + 1)
    cases = []

    def add(s: str):
        c = loads_text_case(s)
        if c is not None:
            cases.append(c)

    for s in number_texts():
        add(s)
        add('[' + s + ']')
        add('{"a": ' + s + '}')
        add(' ' + s + ' ')
    for s in literal_texts():
        add(s)
    # each code point that is not a JSON character, at several places
    odd = ['\x00', '\x01', '\x08', '\x0b', '\x0c', '\x1f', '\x7f', '\x85', '\xa0', '\u1680', '\u2003', '\u2028', '\u2029', '\u200b', '\u3000', '\ufeff', '\U0001F600', '\ud800', '\udc00']
    for ch in odd:
        for tmpl in ('{}', '[{}]', '"{}"', '["{}"]', '{{"{}": 1}}', '1{}', '{}1', '[1,{}2]', '{{"a"{}:1}}', 'tru{}', '"\\u00{}0"', '"\\{}"', '"a\\{}b"'):
            add(tmpl.format(ch))
    # each control character inside a string, as a raw character and escaped
    for i in range(0x20):
        add('"' + chr(i) + '"')
        add('"a' + chr(i) + 'b"')
        add('"\\u%04x"' % i)
        add('{"' + chr(i) + '": 1}')
    for i in range(0x80):
        add('"\\' + chr(i) + '"')
    # whitespace between each pair of tokens
    for ws in (' ', '\t', '\n', '\r', ' \t\n\r', '\x0b', '\x0c', '\xa0'):
        add(ws + '{' + ws + '"a"' + ws + ':' + ws + '[' + ws + '1' + ws + ',' + ws + '2' + ws + ']' + ws + '}' + ws)
    # valid texts from dumps with each option, then the same text mutated
    seeds = []
    for i in range(1500 * SCALE):
        v = random_value(rng, exotic=0.0)
        combo = rng.choice(COMBOS)
        try:
            s = py_dumps(v, *combo, None)
        except (TypeError, ValueError):
            continue
        if len(s) > 4000:
            continue
        seeds.append(s)
        add(s)
    chars = list('[]{},:"\'\\/-+.eE0189nulrtfaNIy \t\n\r\x0b\x0c\xa0\u2003\ufeff\x00\x1f\x7f#é\u2028') + ['\U0001F600', '\ud800', '\udc00', 'u', 'u00', 'ud83d', '\\u', '\\ud83d', '\\ude00', 'NaN', 'Infinity', 'true', 'null', ',]', ',}', '":', '"']
    for i in range(4500 * SCALE):
        s = rng.choice(seeds)
        for _ in range(rng.randint(1, 3)):
            op = rng.random()
            pos = rng.randint(0, len(s))
            if op < 0.25 and s:
                p = min(pos, len(s) - 1)
                s = s[:p] + s[p + 1:]
            elif op < 0.6:
                s = s[:pos] + rng.choice(chars) + s[pos:]
            elif op < 0.8 and s:
                p = min(pos, len(s) - 1)
                s = s[:p] + rng.choice(chars) + s[p + 1:]
            elif op < 0.9:
                s = s[:pos]
            else:
                s = s + rng.choice(chars)
        add(s)
    # short random texts from the JSON alphabet
    alphabet = list('[]{},:"\\ 01-.eEnultrfa') + ['true', 'null', 'false', '\\u', 'NaN', '-Infinity']
    for i in range(2500 * SCALE):
        add(''.join(rng.choice(alphabet) for _ in range(rng.randint(0, 10))))
    return cases


def parts_case(parts, build) -> dict:
    s = ''.join(t * n for t, n in parts)
    case = loads_case({'parts': [[cps(t), n] for t, n in parts]}, s)
    assert case is not None
    return case


def make_big_loads() -> list[dict]:
    cases = []
    # 1 MB strings
    for unit, n in (('a', 500_000), ('é', 500_000), ('\U0001F600', 250_000), ('\\n', 250_000), ('\\u00e9', 160_000), ('\\ud83d\\ude00', 90_000), (' ', 500_000)):
        cases.append(parts_case([('"', 1), (unit, n), ('"', 1)], None))
    cases.append(parts_case([('"', 1), ('a', 1_000_000), ('"', 1)], None))
    cases.append(parts_case([('"', 1), ('a', 1_000_000)], None))
    cases.append(parts_case([('[', 1), ('"a",', 3000), ('"b"]', 1)], None))
    cases.append(parts_case([('{', 1), ('"k":"v",', 1), ('"z":1}', 1)], None))
    cases.append(parts_case([(' ', 1_000_000), ('1', 1)], None))
    cases.append(parts_case([('1', 1_000_000)], None))
    cases.append(parts_case([('[', 1), ('1,', 2000), ('1]', 1)], None))
    # nesting at the limit of Python's C scanner: 9997 levels work, 9998 do not
    for n in (50, 1000, 5000, 9996, 9997, 9998, 9999, 20000):
        cases.append(parts_case([('[', n), (']', n)], None))
        cases.append(parts_case([('[', n), ('1', 1), (']', n)], None))
        cases.append(parts_case([('{"a":', n), ('1', 1), ('}', n)], None))
        cases.append(parts_case([('[', n)], None))
        cases.append(parts_case([('{"a":[', n // 2), ('1', 1), (']}', n // 2)], None))
        cases.append(parts_case([('[{"a":', n // 2), ('1', 1), ('}]', n // 2)], None))
    return cases


# ---------------------------------------------------------------------------------------------
# ast.literal_eval
# ---------------------------------------------------------------------------------------------

def py_eval(s: str) -> dict | None:
    try:
        v = ast.literal_eval(s)
    except SyntaxError as e:
        return {'e': 'SyntaxError'}
    except UnicodeEncodeError as e:
        return {'e': 'UnicodeEncodeError', 'msg': cps(str(e))}
    except TypeError as e:
        return {'e': 'TypeError', 'msg': cps(str(e))}
    except ValueError as e:
        return {'e': 'ValueError'}
    except (MemoryError, RecursionError):
        # Python gives up on a chain of thousands of operators. The port reads such a chain with a loop.
        return None
    if result_has_joinable(v):
        return None
    if isinstance(v, complex):
        return {'unsupported': 'complex'}
    if v is Ellipsis:
        return {'unsupported': 'Ellipsis'}
    if contains_unsupported(v):
        return {'unsupported': 'nested'}
    return {'r': encode(v)}


def contains_unsupported(v) -> bool:
    if isinstance(v, (complex, type(Ellipsis))):
        return True
    if isinstance(v, (list, tuple, set, frozenset)):
        return any(contains_unsupported(x) for x in v)
    if isinstance(v, dict):
        return any(contains_unsupported(k) or contains_unsupported(x) for k, x in v.items())
    return False


def eval_case(s: str) -> dict | None:
    if has_joinable_surrogates(s):
        return None
    case = {'t': text(s)}
    result = py_eval(s)
    if result is None:
        return None
    case.update(result)
    return case


def eval_texts() -> list[str]:
    nums = ['0', '1', '-1', '+1', '--1', '-+1', '+-1', '- 1', '-  1', '1_000', '1__0', '1_', '_1', '0_0', '00', '0_1', '01', '007', '0x10', '0XfF', '0x_f', '0x', '0xg', '0o17', '0O17', '0o8', '0b101', '0B101', '0b12',
            '1.5', '1.', '.5', '.', '1e5', '1E5', '1e+5', '1e-5', '1_0.0_1e1_0', '1e', '1e+', '1.5e', '1e400', '-1e400', '1e-400', '5e-324', '1.7976931348623157e308', '0.1', '1j', '1.5J', '1+2j', '1-2j', '-1+2j', '1+2', '1-2',
            '1+2+3', '2*3', '2**3', '1 if 2 else 3', '1 < 2', '1 == 1', 'not 1', 'not True', '~1', '-(1)', '-(-1)', '+(1.5)', '-(1,)', '(1)', '((1))', '(((1)))', '-True', '-None', '-"a"', '-b"a"', '- -1',
            str(LONG_MAX), str(LONG_MIN), str(LONG_MAX + 1), str(LONG_MIN - 1), '9' * 400, '1' * 4300, '1' * 4301, '-' + '1' * 4300, '-' + '1' * 4301, '0x' + 'f' * 5000, '0o' + '7' * 5000, '0b' + '1' * 20000, '1e5j', '0j', '0x1j', '1.j',
            '1 .real', '1.real', '1..real', '1_000.000_1', '1_000e1_0', '0e0', '00.5', '0_0.5', '012.5', '09.5', '1if 2else 3', '1or 2', '1and 2', '1in 2', '1is 2', '0x1for x in y', '0b1or 2']
    names = ['None', 'True', 'False', 'none', 'true', 'false', 'nan', 'inf', '-inf', 'Infinity', 'NaN', 'foo', 'Foo', '_', 'é', 'é1', 'a\u00b7', '日本', 'print', 'print(1)', 'foo(1)', 'foo()', 'set()', 'set([1])', 'set(1)', 'frozenset()',
             'frozenset([1])', 'dict()', 'list()', 'tuple()', 'set ( )', 'set\n()', 'set(\n)', 'set(a=1)', 'set(*a)', 'a.b', 'a[1]', 'a[1:2]', 'a[:]', 'a[::2]', 'a[1,2]', 'a[]', 'a.', '.a', 'a()()', 'a.b.c(d)[e]', 'lambda: 1', 'lambda x: x', 'lambda x, y=1, *a, **k: 0',
             '[x for x in y]', '{x for x in y}', '{x: 1 for x in y}', '(x for x in y)', '[x for x in y if x]', '[x for x in y for z in x]', '[x async for x in y]', 'await x', '(yield)', '(yield 1)', 'yield', 'x := 1', '(x := 1)', '[x := 1]', '[(x := 1)]',
             '1 if 2 else 3', 'a if b else c if d else e', 'a or b', 'a and b', 'a or b and c', 'not a', 'a < b < c', 'a is not b', 'a not in b', 'a in b', 'a == b', 'a != b', 'a <> b', 'a | b', 'a ^ b', 'a & b', 'a << b', 'a >> b', 'a @ b', 'a // b', 'a % b',
             '*a', '*a, b', 'a, *b', '[*a]', '(*a,)', '{*a}', '{**a}', '{1: 2, **a}', '{**a, 1: 2}', 'f(**a)', 'f(*a)', 'f(a=1)', 'f(a, b=1)', 'f(a=1, b)', 'f(a for a in b)', '...', '[...]', '(...)', '{...}', '{...: ...}', 'Ellipsis', '...  ', '(..., 1)',
             'async', 'await', 'class', 'def', 'if', 'else', 'for', 'in', 'is', 'not', 'and', 'or', 'import', 'from', 'pass', 'return', 'del', 'global', 'match', 'case', 'type', 'print foo', 'x y', 'x\ny', 'x;y', 'x = 1', 'x += 1', 'del x', 'import x', 'pass',
             'if x: 1', 'x: int', 'x: int = 1', 'with x: pass', '@x', '->', '=', '==', '+', '-', '*', '**', '/', '//', '%', '@', '&', '|', '^', '~', '<<', '>>', '<', '>', '<=', '>=', '!=', '!', '$', '?', '`', '\\', ';', ':', ',', '.', '..', '....', '(', ')', '[', ']', '{', '}', ')(', '][', '}{']
    strings = ["''", '""', "'a'", '"a"', "'a\"b'", '"a\'b"', "'a\\'b'", '"a\\"b"', "'\\\\'", "'\\n'", "'\\t'", "'\\r'", "'\\a'", "'\\b'", "'\\f'", "'\\v'", "'\\0'", "'\\00'", "'\\000'", "'\\0000'", "'\\1'", "'\\12'", "'\\123'", "'\\1234'", "'\\377'", "'\\400'", "'\\777'",
               "'\\8'", "'\\9'", "'\\x41'", "'\\x4'", "'\\x'", "'\\xg0'", "'\\xFF'", "'\\u0041'", "'\\u004'", "'\\u'", "'\\u00e9'", "'\\u00E9'", "'\\U00000041'", "'\\U0001F600'", "'\\U0001f600'", "'\\U0010FFFF'", "'\\U00110000'", "'\\U000000'", "'\\ud800'", "'\\udc00'",
               "'\\ud83d\\ude00'", "'\\ude00\\ud83d'", "'\\ud800x'", "'\\d'", "'\\ '", "'\\/'", "'\\\"'", "'\\\n'", "'a\\\nb'", "'a\\\r\nb'", "'a\\\rb'", "'\\\\\n'", "'abc", "abc'", "'", '"', "'a\nb'", "'a\rb'", '"""a\nb"""', "'''a\nb'''", "'''a'''", "'''a\\'''", "'''a'b'''", "'''a''b'''", "'''a", "'''''", "''''", "'''''''",
               "r'a\\nb'", "R'a\\nb'", "r'\\'", "r'\\''", "r'a\\\\'", "u'a'", "U'a'", "b'a'", "B'a'", "br'a\\n'", "rb'a\\n'", "bR'a'", "Rb'a'", "BR'a'", "ur'a'", "ru'a'", "bu'a'", "ub'a'", "f'a'", "F'a'", "fr'a'", "rf'a'", "Rf'a'", "fb'a'", "bf'a'", "uf'a'",
               "f'{x}'", "f'{{x}}'", "f'{x!r}'", "f'{x:>{y}}'", "f'{'", "f'}'", "f'{}'", "f'{x'", "f'{x}{'", "'a' 'b'", "'a' \"b\"", "'a'\"b\"'c'", "'a'\n'b'", "('a'\n'b')", "['a'\n'b']", "['a' 'b', 'c']", "'a' b'b'", "b'a' 'b'", "b'a' b'b'", "u'a' 'b'", "r'a' 'b\\n'", "f'a' 'b'", "'a' f'b'",
               "b'\\x41'", "b'\\xff'", "b'\\x4'", "b'\\u0041'", "b'\\n'", "b'\\400'", "b'\\777'", "b'\\8'", "b'é'", "b'\\xe9'", "b'a\x80'", "b'\\''", "b\"\\\"\"", "b'\\\\'", "b''", 'b""', "b'a' b''", "br'\\x41'",
               "'é'", "'日本語'", "'\U0001F600'", "'a\u0301'", "'\\N{DIGIT ONE}'", "'\\N{LATIN SMALL LETTER A}'", "'\\N{FOO}'", "'\\N'", "'\\N{'", "'\\N{}'", "'\x00'", "'\x01'", "'\x7f'", "'\x0b'", "'\x0c'", "'\x1f'", "'\u2028'", "'\ud800'", "'\udc00'", "'a\ud800b'", "\ud800", "'a' \ud800", "# \ud800\n1"]
    containers = ['[]', '()', '{}', 'set()', '[1]', '[1,]', '[1, 2]', '[1, 2,]', '[1,,2]', '[,]', '[,1]', '[1 2]', '[1, 2', '[1, 2]]', '[[1], [2]]', '[[[]]]', '[()]', '[{}]', '[{}, ()]', '((),)', '((), ())', '(1,)', '(1)', '(1, 2)', '(1, 2,)', '(1,,2)', '(,)', '(1 2)', '(1, 2', '1, 2', '1,', '1, 2,', ',', ',1', '1,,2',
                  '"a", "b"', "'a',", '{1}', '{1,}', '{1, 2}', '{1, 1}', '{1, 1.0, True}', '{1,,2}', '{,}', '{1 2}', '{(1,2)}', '{(1,[2])}', '{[1]}', '{{1}}', '{{}}', '{1: 2}', '{1: 2,}', '{1: 2, 3: 4}', '{1: 2, 1: 3}', '{1: 2, 1.0: 3}', '{1: 2, True: 3}', '{True: 1, 1: 2}', '{0: 1, False: 2, 0.0: 3}', '{"a": 1, "a": 2}', '{"a": 1, "b": 2, "a": 3}',
                  "{'a': 1}", '{"a": [1, 2], "b": {"c": None}}', "{'a': 1, 'b': True, 'c': None}", '{1: 2 3: 4}', '{1: }', '{: 1}', '{:}', '{1:}', '{1: 2,,}', '{1:2 3}', '{"a"}', '{"a": 1, "b"}', '{"a", "b": 1}', '{[1]: 2}', '{{}: 1}', '{(1, 2): 3}', '{(1, [2]): 3}', '{None: 1}', '{b"a": 1}', '{1.5: 1}', '{float: 1}', '{nan: 1}', '{(): 1}', '{frozenset(): 1}',
                  '{"a": {"b": {"c": {}}}}', '[{"a": 1}, {"b": 2}]', "[{'a': 1}, {'b': 2}]", "[{'a': 1, 'b': [1, 2, {'c': 'd'}]}]", "{'a': (1, 2), 'b': {1, 2}}", '[[], [[]], [[], []]]', '[1, [2, [3, [4, [5]]]]]', '[1, 2, 3][0]', '[1, 2, 3] + [4]', '[1] * 2', '[1, 2, 3][::-1]', '(1, 2, 3)[0]', '{1: 2}[1]', '{1: 2}.get(1)',
                  '[1, 2, ...]', '[1, ..., 2]', '[...]', "[{'a': 1}, ...]", '[1, 2, 3, ]', '[ 1 , 2 ]', '[\n1,\n2\n]', '(\n1,\n2\n)', '{\n1: 2\n}', '[1, # c\n2]', '[1,\\\n2]', '[1,\n# c\n2]', '[\n]', '(\n)', '{\n}', '[1\n,\n2]',
                  '[' * 199 + ']' * 199, '[' * 200 + ']' * 200, '[' * 201 + ']' * 201, '(' * 200 + ')' * 200, '(' * 201 + ')' * 201, '{' * 1, '{1:' * 200 + '1' + '}' * 200, '{1:' * 201 + '1' + '}' * 201, '[(' * 100 + ']' * 0 + ')]' * 100, '[(' * 101 + ')]' * 101, '[{' * 100 + '}]' * 100,
                  '[' * 150 + '1' + ']' * 150, '(' * 150 + '1' + ')' * 150, '(' * 150 + '1,' + ')' * 150]
    spaces = ['', ' ', '  ', '\t', ' \t ', '\n', '\n\n', '\r', '\r\n', '\f', ' \f', '\f ', '\x0b', '\xa0', '\u2003', '\ufeff', '\u200b', '#', '# c', '#c\n', '# c\n1', '1 # c', '1 #', '1\n#', '1\n# c', '1\n# c\n', '1 # c\n', '1\n\n', '1\n \n', '1\n  \n', '1\n\t\n', '1\n\f\n', '1\n  # c',
              '1\n  2', '1\n2', '1\n\n2', '1;', '1; 2', '1 \\', '1 \\\n', '1 \\\n2', '1 \\\n 2', '1 \\ \n2', '\\\n1', '\\\n\\\n1', ' \\\n1', '\n1', '\n\n1', '\n \n1', '\n  1', '\n\t1', '\n\f1', '\n1\n', '\n#c\n1', '\n #c\n1', '  1', '\t1', ' \t1', '\f1', '\f 1', ' \f1', '\t\f1', '\f\t1', ' \n1', '\t\n1', '  \n  1',
              '1\r\n', '1\r', '1\r\r', '(1,\r\n2)', "'a\r\nb'", "'''a\r\nb'''", "'''a\rb'''", '1 2', '1\n\n\n', '1  \n  ']
    garbage = ['Here is the list: [1, 2]', 'The answer is: {"a": 1}', '```json\n[1]\n```', '```python\n[1]\n```', '[1, 2] is the list', '[1] [2]', "[{'a': 1}] extra", "Sure! Here you go:\n[{'a': 1}]", '<think>x</think>[1]', "[{'candidate_id': 1, 'name': 'x'}, {'candidate_id': 2, 'name': 'y'}",
               "[{'a': 1}, {'b': 2}", "[{'name': 'it's'}]", '[{"name": "it\'s"}]', "[{'name': \"it's\"}]", "[{'a': 'b' 'c'}]", "[{'a': 1} {'b': 2}]", "[{'a': 1}; {'b': 2}]", '[{"a": 1}, {"b": 2}]', '{"a": 1}\n{"b": 2}', '[1, 2, 3', '1, 2, 3]', '[true, false, null]', '[True, False, None]', '[TRUE]', '[1.5, -2.5, 1e3]', '[NaN]', '[nan]', '[inf, -inf]',
               "[{'a': datetime.datetime(2025, 1, 1)}]", "[{'a': datetime.date(2025, 1, 1)}]", "[{'a': UUID('x')}]", "[{'a': <Foo>}]", "[{'a': 1.0}]", "[{'a': 1e+22}]", "[{'a': -0.0}]", "{'a': float('nan')}", "[{'a': b'x'}]", "[{'a': {1, 2}}]", "[{'a': frozenset({1})}]", "[{'a': (1, 2)}]", "[{'a': None}]", "[{'a': 'é'}]", "[{'a': '😀'}]", "[{'a': '\\ud83d\\ude00'}]",
               "[{'a': 'line\\nbreak'}]", "[{'a': 'tab\\there'}]", "[{'a': 'quote\\'s'}]", '[{\'a\': "dq\\"s"}]', "[{'a': '\\\\'}]", "[{'a': 'x' * 3}]", "[{'a': 1 + 2}]", "[{'a': -1}]", "[{'a': +1}]", "[{'a': 1_000}]", "[{'a': 0x10}]", "[{'a': 1j}]", "[{'a': ...}]", "[{...}]", '[{"a": ...}]',
               'null', 'true', 'false', 'None True False', '"a" + "b"', '"a" * 3', "'a' % 1", '[1] + [2]', '(1, 2)[0]', '__import__("os")', 'open("x")', 'exec("1")', 'eval("1")', '[].__class__', '().__class__.__bases__', '1 .__class__', '"{}".format(1)', 'f"{1}"', '(lambda: 1)()', '[i for i in range(3)]', 'x if y else z']
    return nums + names + strings + containers + spaces + garbage


def repr_seeds(rng: random.Random) -> list[str]:
    out = []
    for _ in range(900 * SCALE):
        v = random_value(rng, exotic=0.15)
        try:
            out.append(repr(v))
        except Exception:
            pass
    return [s for s in out if len(s) < 3000 and not has_joinable_surrogates(s)]


def make_eval() -> list[dict]:
    rng = random.Random(SEED + 2)
    cases = []

    def add(s: str):
        if '\\N' in s:
            # A \N{name} escape needs the table of Unicode names. The port does not have it.
            return
        c = eval_case(s)
        if c is not None:
            cases.append(c)

    for s in eval_texts():
        add(s)
    seeds = repr_seeds(rng)
    for s in seeds:
        add(s)
    # each type of repr output through json too
    for _ in range(300):
        add(json.dumps(random_value(rng, exotic=0.0), ensure_ascii=rng.random() < 0.5))
    chars = list('[]{}(),:;\'"\\/-+.eE0189jJxXoObB_ \t\n\r\f\x0b\xa0\x00#é\u2028$?!`@=<>~*%&|^') + ['\U0001F600', '\ud800', 'None', 'True', 'False', 'nan', 'u', 'b', 'r', 'f', "'''", '"""', '\\x', '\\u', '\\N{', '...', ', ', ': ', 'lambda', ' for ', ' if ', ' else ', 'not ', ' in ', '**', '//', '->', ':=']
    for _ in range(5200 * SCALE):
        s = rng.choice(seeds)
        for _ in range(rng.randint(1, 3)):
            op = rng.random()
            pos = rng.randint(0, len(s))
            if op < 0.25 and s:
                p = min(pos, len(s) - 1)
                s = s[:p] + s[p + 1:]
            elif op < 0.6:
                s = s[:pos] + rng.choice(chars) + s[pos:]
            elif op < 0.8 and s:
                p = min(pos, len(s) - 1)
                s = s[:p] + rng.choice(chars) + s[p + 1:]
            elif op < 0.9:
                s = s[:pos]
            else:
                s = s + rng.choice(chars)
        add(s)
    alphabet = list("[]{}(),:'\" 0123456789.-+_eEjJxXbBoO\\ntrfuNone") + ['None', 'True', 'False', 'b', 'r', 'f', 'u', '\\n', '\\x41', '...', ' if ', ' else ', ' for ', ' in ', 'not ', '**', '#', '\n']
    for _ in range(3000 * SCALE):
        add(''.join(rng.choice(alphabet) for _ in range(rng.randint(0, 9))))
    return cases


def make_big_eval() -> list[dict]:
    cases = []
    for parts in ([("'", 1), ('a', 1_000_000), ("'", 1)], [('[', 1), ("'a',", 3000), (']', 1)], [('(', 199), ('1', 1), (')', 199)], [('(', 200), ('1', 1), (')', 200)], [('(', 201), ('1', 1), (')', 201)],
                  [('[', 200), (']', 200)], [('[', 201), (']', 201)], [('{', 200), ('}', 0)], [('-', 1000), ('1', 1)], [('-', 100), ('1', 1)], [('not ', 1000), ('1', 1)], [('1+', 1000), ('1', 1)], [("'a' ", 5000)], [('{1:', 199), ('1', 1), ('}', 199)],
                  [('{1:', 200), ('1', 1), ('}', 200)], [('{1:', 201), ('1', 1), ('}', 201)], [('1,', 3000)], [('0', 1), ('_0', 100000)], [('1', 1), ('_1', 2000)]):
        s = ''.join(t * n for t, n in parts)
        case = {'t': {'parts': [[cps(t), n] for t, n in parts]}}
        result = py_eval(s)
        assert result is not None
        case.update(result)
        cases.append(case)
    return cases


def build_values() -> dict:
    return {
        'cases': {
            'dumps': make_dumps(),
            'dumps_pool': POOL,
            'dumps_deep': make_deep_dumps(),
            'loads': make_loads(),
            'loads_big': make_big_loads(),
            'literal_eval': make_eval(),
            'literal_eval_big': make_big_eval(),
        }
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    text_out = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=False) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text_out:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_pyjson_values: up to date')
        return 0
    VALUES_PATH.write_text(text_out, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text_out)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
