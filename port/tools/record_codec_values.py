"""Records what the Python judge codec writes for generated values (plan 0003, task P7).

Two files come out of a run:

- tests/Port.Evaluation/JudgeHost/python-codec-values.jsonl: one line for each value, with the value as
  argument tags in "input" and the text of json.dumps(encode(value), ensure_ascii=True) in "output".
  That is how port/judge/host-python/host.py writes a result. A test decodes each input with the C# decoder,
  encodes it with the C# codec, and compares the text.
- tests/Port.Evaluation/JudgeHost/python-neo4j-errors.json: the class name that the neo4j driver of the oracle
  gives to a server error with each status code (Neo4jError._hydrate_neo4j). The codes are the keys of the
  driver's own tables and a few codes outside them.

Run it with the oracle interpreter, and with -B so that no __pycache__ folder appears under port/judge/:

    .upstream/graphiti/.venv/bin/python -B port/tools/record_codec_values.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_codec_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.

The input uses the argument tags of port/judge/README.md, and a few more that only the test knows:

    {"$date": "2026-03-01"}                 a date
    {"$enum": "test:Color", "value": v}     a member of the test enum Color
    {"$model": "test:Point", "data": {}}    a test dataclass or pydantic model, with its fields in order
    {"$ref": "test:class:Name"}             a class
    {"$ref": "test:exc:Name"}               an exception of a built-in class
    {"$ref": "test:unknown:Opaque"}         an object of a class that the codec does not know
"""
from __future__ import annotations

import argparse
import dataclasses
import enum
import json
import math
import random
import struct
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from pydantic import BaseModel, ConfigDict

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'port' / 'judge' / 'host-python'))

from codec import encode  # noqa: E402

OUT = ROOT / 'tests' / 'Port.Evaluation' / 'JudgeHost' / 'python-codec-values.jsonl'
NEO4J_OUT = ROOT / 'tests' / 'Port.Evaluation' / 'JudgeHost' / 'python-neo4j-errors.json'


class Color(enum.Enum):
    RED = 'red'
    ONE = 1
    BIG = -(2**63)


@dataclasses.dataclass
class Point:
    x: Any
    y: Any


class Box(BaseModel):
    model_config = ConfigDict(extra='allow', arbitrary_types_allowed=True)
    name: Any
    point: Any = None
    items: Any = None


class Opaque:
    pass


EXCEPTIONS = [ValueError, KeyError, TypeError, ZeroDivisionError, AttributeError, NotImplementedError,
              OverflowError, RuntimeError, IndexError]


def to_arg(value: Any) -> Any:
    """The value as argument tags, so that the C# decoder builds the same value."""
    if value is None or isinstance(value, (bool, str)):
        return value
    if isinstance(value, enum.Enum):
        return {'$enum': f'test:{type(value).__name__}', 'value': value.value}
    if isinstance(value, int):
        return value
    if isinstance(value, float):
        if math.isnan(value) or math.isinf(value):
            return {'$float': repr(value)}
        return value
    if isinstance(value, datetime):
        return {'$dt': value.isoformat()}
    if isinstance(value, date):
        return {'$date': value.isoformat()}
    if isinstance(value, bytes):
        return {'$bytes': value.hex()}
    if isinstance(value, tuple):
        return {'$tuple': [to_arg(v) for v in value]}
    if isinstance(value, list):
        return [to_arg(v) for v in value]
    if isinstance(value, (set, frozenset)):
        # The order of a set depends on the hash seed of the process. Sort it, so that a run gives the same file.
        return {'$set': sorted((to_arg(v) for v in value), key=lambda v: json.dumps(v, sort_keys=True))}
    if isinstance(value, dict):
        if all(isinstance(k, str) for k in value):
            return {k: to_arg(v) for k, v in value.items()}
        return {'$dict': [[to_arg(k), to_arg(v)] for k, v in value.items()]}
    if isinstance(value, type):
        return {'$ref': f'test:class:{value.__name__}'}
    if isinstance(value, BaseException):
        return {'$ref': f'test:exc:{type(value).__name__}'}
    if isinstance(value, Box):
        fields = {name: getattr(value, name) for name in type(value).model_fields}
        fields.update(value.model_extra or {})
        return {'$model': 'test:Box', 'data': {k: to_arg(v) for k, v in fields.items()}}
    if dataclasses.is_dataclass(value):
        data = {f.name: to_arg(getattr(value, f.name)) for f in dataclasses.fields(value)}
        return {'$model': f'test:{type(value).__name__}', 'data': data}
    if isinstance(value, Opaque):
        return {'$ref': 'test:unknown:Opaque'}
    raise TypeError(type(value))


class Gen:
    def __init__(self, seed: int):
        self.r = random.Random(seed)

    def int(self) -> int:
        r = self.r
        choice = r.randrange(6)
        if choice == 0:
            return r.randint(-10, 10)
        if choice == 1:
            return r.choice([2**63 - 1, -(2**63), 2**63, -(2**63) - 1, 2**53, 2**53 + 1, 2**64, 0])
        if choice == 2:
            return r.randint(-(2**300), 2**300)
        return r.randint(-(2**62), 2**62)

    def float(self) -> float:
        r = self.r
        choice = r.randrange(8)
        if choice == 0:
            return r.choice([0.0, -0.0, 1.0, -1.0, 0.1, 100.0, 1e16, 1e15, 1e-5, 1e-4, 5e-324, 1.7976931348623157e308,
                             2.0**-25, 1e22, 123456789012345680.0, 0.30000000000000004, float('nan'), float('inf'),
                             float('-inf')])
        if choice == 1:
            return float(r.randint(-(10**18), 10**18))
        if choice == 2:
            return r.uniform(-1000, 1000)
        while True:
            value = struct.unpack('<d', r.getrandbits(64).to_bytes(8, 'little'))[0]
            if not math.isnan(value):
                return value

    def char(self) -> str:
        r = self.r
        pool = r.randrange(9)
        if pool == 0:
            return chr(r.randrange(0, 32))
        if pool == 1:
            return r.choice(['"', '\\', '/', '\x7f', ' ', ' ', '~', ' '])
        if pool == 2:
            return chr(r.randrange(0x80, 0x100))
        if pool == 3:
            return chr(r.randrange(0x100, 0xD800))
        if pool == 4:
            return chr(r.randrange(0xD800, 0xE000))  # a lone surrogate
        if pool == 5:
            return chr(r.randrange(0xE000, 0x10000))
        if pool == 6:
            return chr(r.randrange(0x10000, 0x110000))
        return chr(r.randrange(0x20, 0x7F))

    def str(self) -> str:
        return ''.join(self.char() for _ in range(self.r.randrange(0, 12)))

    def bytes(self) -> bytes:
        return bytes(self.r.randrange(256) for _ in range(self.r.randrange(0, 20)))

    def datetime(self) -> datetime:
        r = self.r
        value = datetime(r.randint(1, 9999), r.randint(1, 12), r.randint(1, 28), r.randrange(24), r.randrange(60),
                         r.randrange(60), r.choice([0, 0, r.randrange(1_000_000)]))
        zone = r.randrange(6)
        if zone == 0:
            return value
        if zone == 1:
            return value.replace(tzinfo=timezone.utc)
        if zone == 2:
            return value.replace(tzinfo=timezone(timedelta(seconds=r.randint(-86399, 86399))))
        if zone == 3:
            return value.replace(tzinfo=timezone(timedelta(seconds=r.randint(-86399, 86399), microseconds=r.randrange(1_000_000))))
        return value.replace(tzinfo=timezone(timedelta(minutes=r.choice([-720, -330, 60, 330, 345, 840]))))

    def date(self) -> date:
        r = self.r
        return date(r.randint(1, 9999), r.randint(1, 12), r.randint(1, 28))

    def scalar(self) -> Any:
        r = self.r
        return [None, True, False, self.int(), self.float(), self.str(), self.bytes(), self.datetime(), self.date(),
                r.choice(list(Color))][r.randrange(10)]

    def hashable(self, depth: int = 0) -> Any:
        r = self.r
        choice = r.randrange(9)
        if choice == 0 and depth < 2:
            return tuple(self.hashable(depth + 1) for _ in range(r.randrange(0, 4)))
        if choice == 1:
            return self.str()
        if choice == 2:
            return self.float()
        if choice == 3:
            return r.choice([None, True, False])
        if choice == 4:
            return self.datetime()
        if choice == 5:
            return self.bytes()
        if choice == 6:
            return r.choice(list(Color))
        return self.int()

    def key_text(self) -> str:
        # A key that starts with $ is a plain key, unless it is a tag. Tags are not generated here.
        text = self.str()
        return self.r.choice([text, text, '$x' + text, 'k'])

    def value(self, depth: int = 0) -> Any:
        r = self.r
        if depth >= 3:
            return self.scalar()
        choice = r.randrange(10)
        if choice == 0:
            return [self.value(depth + 1) for _ in range(r.randrange(0, 4))]
        if choice == 1:
            return tuple(self.value(depth + 1) for _ in range(r.randrange(0, 4)))
        if choice == 2:
            return {self.hashable() for _ in range(r.randrange(0, 5))}
        if choice == 3:
            return {self.key_text(): self.value(depth + 1) for _ in range(r.randrange(0, 4))}
        if choice == 4:
            return {self.hashable(): self.value(depth + 1) for _ in range(r.randrange(0, 4))}
        if choice == 5:
            return Point(self.value(depth + 1), self.value(depth + 1))
        if choice == 6:
            box = Box(name=self.str(), point=Point(self.int(), self.value(depth + 1)), items=[self.value(depth + 1)])
            if r.randrange(2):
                box.extra_field = self.value(depth + 1)  # type: ignore[attr-defined]
            return box
        return self.scalar()


def records() -> list[dict]:
    g = Gen(20261126)
    kinds: list[tuple[str, int, Any]] = [
        ('int', 300, g.int),
        ('float', 400, g.float),
        ('str', 400, g.str),
        ('bytes', 100, g.bytes),
        ('datetime', 300, g.datetime),
        ('date', 100, g.date),
        ('list', 200, lambda: [g.value(1) for _ in range(g.r.randrange(0, 5))]),
        ('tuple', 200, lambda: tuple(g.value(1) for _ in range(g.r.randrange(0, 5)))),
        ('set', 300, lambda: {g.hashable() for _ in range(g.r.randrange(0, 7))}),
        ('frozenset', 50, lambda: frozenset(g.hashable() for _ in range(g.r.randrange(0, 5)))),
        ('dict_str', 300, lambda: {g.key_text(): g.value(1) for _ in range(g.r.randrange(0, 5))}),
        ('dict_mixed', 300, lambda: {g.hashable(): g.value(1) for _ in range(g.r.randrange(1, 6))} | {g.str(): g.int()}),
        ('enum', 50, lambda: g.r.choice(list(Color))),
        ('model', 300, lambda: g.value(2) if g.r.randrange(3) else Point(g.value(1), g.value(1))),
        ('nested', 300, g.value),
    ]
    out: list[dict] = []
    for kind, count, make in kinds:
        for _ in range(count):
            value = make()
            out.append({'kind': kind, 'input': to_arg(value), 'output': json.dumps(encode(value), ensure_ascii=True)})
    fixed: list[Any] = [
        [], (), {}, set(), frozenset(), b'', '', [[]], {'': {}}, {(): ()}, {None: None, 'a': 1},
        {1: 'int', 'b': 'str', (1, 2): 'tuple', None: 'none', 2.5: 'float'},
        {float('nan'), 1, '1', (1,), None}, {True: 'a', 2: 'b'},
        Point, Box, Opaque, *EXCEPTIONS, *[e('message') for e in EXCEPTIONS], Opaque(), [Opaque()],
        '\ud800', '\udfff', '😀', 'a\ud800b', '\udc00\ud800', '\x00\x1f\x7f\x80 ',
    ]
    out.extend({'kind': 'fixed', 'input': to_arg(v), 'output': json.dumps(encode(v), ensure_ascii=True)} for v in fixed)
    return out


def neo4j_errors() -> dict:
    import neo4j
    from neo4j import exceptions

    codes = [*exceptions._client_errors, *exceptions._transient_errors, *exceptions._ERROR_REWRITE_MAP]
    codes += [
        'Neo.ClientError.Statement.ParameterMissing',
        'Neo.ClientError.Security.AuthorizationExpired',
        'Neo.TransientError.General.OutOfMemoryError',
        'Neo.TransientError.Statement.SyntaxError',
        'Neo.DatabaseError.General.UnknownError',
        'Neo.DatabaseError.Statement.SyntaxError',
        'Neo.OtherError.General.Something',
        'Neo.ClientError.Statement',
        'Neo.ClientError.Statement.SyntaxError.Extra',
        'NotACode',
    ]
    rows = [{'code': c, 'python': type(exceptions.Neo4jError._hydrate_neo4j(code=c, message='m')).__name__}
            for c in dict.fromkeys(codes)]
    return {'neo4j': neo4j.__version__, 'codes': rows}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument('--check', action='store_true')
    options = parser.parse_args()
    text = ''.join(json.dumps(r, ensure_ascii=True) + '\n' for r in records())
    neo4j_text = json.dumps(neo4j_errors(), ensure_ascii=True, indent=1) + '\n'
    if options.check:
        same = all(path.exists() and path.read_text(encoding='utf-8') == content
                   for path, content in ((OUT, text), (NEO4J_OUT, neo4j_text)))
        print('record_codec_values: up to date' if same else 'record_codec_values: differs', file=sys.stderr)
        return 0 if same else 1
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    NEO4J_OUT.write_text(neo4j_text, encoding='utf-8')
    print(f'record_codec_values: wrote {len(text.splitlines())} values to {OUT.relative_to(ROOT)}', file=sys.stderr)
    return 0


if __name__ == '__main__':
    sys.exit(main())
