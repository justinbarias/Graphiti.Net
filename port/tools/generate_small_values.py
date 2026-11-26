"""Records what real Python gives for PyEnv, PyHash, PyLruCache, and the error cases of PyRandom.

One file comes out of one run: tests/Graphiti.Net.Property.Tests/python-values.small.json.

    .upstream/graphiti/.venv/bin/python port/tools/generate_small_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_small_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.
Strings are lists of code points. Floats are the 64 bits of the value, as a signed integer.
PyRandom is not Python's Mersenne Twister (GAPS F9, RULES R18): it is xoshiro256** seeded with
SplitMix64. The reference below is an independent Python version of that algorithm. It pins the
sequence, so that a .NET update cannot change it. The 'random_errors' cases come from real Python.
Nothing here uses the network.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import random
import struct
import sys
from functools import lru_cache
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.small.json'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_small_values: needs Python 3.12, found {sys.version.split()[0]}')

LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1
M64 = (1 << 64) - 1


def cps(s: str) -> list[int]:
    return [ord(c) for c in s]


def bits(x: float) -> int:
    return struct.unpack('<q', struct.pack('<d', x))[0]


def err(e: Exception) -> dict:
    return {'e': type(e).__name__, 'msg': cps(str(e))}


def int_result(n: int) -> dict:
    return {'r': n} if LONG_MIN <= n <= LONG_MAX else {'big': str(n)}


def pattern(n: int) -> bytes:
    """The input of the large blake2b cases. The C# test builds the same bytes."""
    return bytes((i * 31 + 7) & 0xFF for i in range(n))


# ---- blake2b ----

def blake2b_cases() -> list[dict]:
    rng = random.Random(20261126)
    cases = []
    for length in (0, 1, 127, 128, 129, 255, 256, 257):
        data = bytes(rng.randrange(256) for _ in range(length))
        for size in range(1, 65):
            cases.append({'hex': data.hex(), 'size': size, 'r': hashlib.blake2b(data, digest_size=size).hexdigest()})
    for length in (2 * 128, 2 * 128 + 1, 1000):
        data = bytes(rng.randrange(256) for _ in range(length))
        for size in (1, 8, 32, 64):
            cases.append({'hex': data.hex(), 'size': size, 'r': hashlib.blake2b(data, digest_size=size).hexdigest()})
    for _ in range(500):
        data = bytes(rng.randrange(256) for _ in range(rng.randrange(0, 600)))
        size = rng.randrange(1, 65)
        cases.append({'hex': data.hex(), 'size': size, 'r': hashlib.blake2b(data, digest_size=size).hexdigest()})
    # The shape that upstream hashes: f'{seed}:{shingle}'.encode(), digest_size=8.
    for seed in (0, 1, 31):
        for shingle in ('', 'abc', 'café \U0001f600', 'é'):
            data = f'{seed}:{shingle}'.encode()
            cases.append({'hex': data.hex(), 'size': 8, 'r': hashlib.blake2b(data, digest_size=8).hexdigest()})
    big = pattern(1 << 20)
    for size in (1, 8, 20, 32, 64):
        cases.append({'pattern': 1 << 20, 'size': size, 'r': hashlib.blake2b(big, digest_size=size).hexdigest()})
    return cases


def blake2b_error_cases() -> list[dict]:
    cases = []
    for size in (0, -1, 65, 1000):
        try:
            hashlib.blake2b(b'abc', digest_size=size)
        except ValueError as e:
            cases.append({'hex': '616263', 'size': size, **err(e)})
    return cases


# ---- environment ----

ENV_VALUES = [
    None, '', '0', '1', '20', ' 20 ', '\t7\n', '-5', '+5', '1_000', '1__0', '_1', 'abc', '20x', 'false', 'False', 'true',
    '0.15', '.5', '5.', '1e3', '1E-2', '1_0.5', 'nan', '-NaN', 'inf', '-Infinity', 'infinity', '  ', ' ',
    '١٢', '１２', '9223372036854775807', '9223372036854775808', '-9223372036854775808',
    '-9223372036854775809', '99999999999999999999', '1e400', '0x10', '1,5', 'café', '\U0001f600', 'é',
]


def env_cases() -> list[dict]:
    cases = []
    for value in ENV_VALUES:
        if value is None:
            os.environ.pop('PYENV_TEST', None)
        else:
            os.environ['PYENV_TEST'] = value
        case = {'value': None if value is None else cps(value)}
        case['str_default'] = {'r': cps(os.environ.get('PYENV_TEST', 'default'))}
        v = os.getenv('PYENV_TEST')
        case['str_none'] = {'r': None if v is None else cps(v)}
        case['truthy'] = {'r': bool(os.getenv('PYENV_TEST', False))}
        try:
            case['int'] = int_result(int(os.getenv('PYENV_TEST', 20)))
        except ValueError as e:
            case['int'] = err(e)
        try:
            x = float(os.getenv('PYENV_TEST', 0.15))
            case['float'] = {'nan': True} if math.isnan(x) else {'r': bits(x), 'nan': False}
        except ValueError as e:
            case['float'] = err(e)
        cases.append(case)
    os.environ.pop('PYENV_TEST', None)
    return cases


# ---- lru_cache ----

def run_trace(maxsize, keys: list[int]) -> dict:
    ran = []

    @lru_cache(maxsize=maxsize)
    def f(k: int) -> int:
        ran.append(k)
        if k < 0:
            raise ValueError(f'negative key {k}')
        return k * k + 1

    steps = []
    for k in keys:
        before = len(ran)
        step = {'key': k}
        try:
            step['r'] = f(k)
        except ValueError:
            step['e'] = 'ValueError'
        info = f.cache_info()
        step.update(ran=len(ran) > before, hits=info.hits, misses=info.misses, size=info.currsize)
        steps.append(step)
    return {'maxsize': maxsize, 'steps': steps}


def lru_cases() -> list[dict]:
    traces = [
        run_trace(2, [1, 2, 1, 3, 2, 1]),
        run_trace(2, [1, 2, 3, 1, 2, 3]),
        run_trace(3, [1, 2, 3, 1, 4, 2, 3, 1, 5, 1]),
        run_trace(1, [1, 1, 2, 2, 1]),
        run_trace(0, [1, 1, 2, 1]),
        run_trace(None, [5, 4, 3, 5, 4, 3, 2, 1, 0, 5]),
        run_trace(2, [-1, -1, 1, -1, 2, 3, -1]),
        run_trace(None, [-1, 4, -1, 4]),
        run_trace(0, [-1, -1]),
        run_trace(-3, [1, 1]),
        run_trace(512, list(range(600)) + [0, 599, 88, 89, 511, 512]),
    ]
    rng = random.Random(99)
    for _ in range(40):
        maxsize = rng.choice([None, 0, 1, 2, 3, 5])
        traces.append(run_trace(maxsize, [rng.randrange(-2, 9) for _ in range(60)]))
    return traces


# ---- random ----

def splitmix64(state: int) -> tuple[int, int]:
    state = (state + 0x9E3779B97F4A7C15) & M64
    z = state
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & M64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & M64
    return state, z ^ (z >> 31)


def rotl(x: int, k: int) -> int:
    return ((x << k) | (x >> (64 - k))) & M64


class Xoshiro:
    def __init__(self, seed: int) -> None:
        state = seed & M64
        self.s = []
        for _ in range(4):
            state, out = splitmix64(state)
            self.s.append(out)

    def next(self) -> int:
        s = self.s
        result = (rotl((s[1] * 5) & M64, 7) * 9) & M64
        t = (s[1] << 17) & M64
        s[2] ^= s[0]
        s[3] ^= s[1]
        s[1] ^= s[2]
        s[0] ^= s[3]
        s[2] ^= t
        s[3] = rotl(s[3], 45)
        return result


def random_sequences() -> list[dict]:
    cases = []
    for seed in (0, 1, -1, 42, 12345678901234, LONG_MAX, LONG_MIN, 2**40 + 3, 7, 99, 2026):
        x = Xoshiro(seed)
        ulongs = [x.next() for _ in range(16)]
        y = Xoshiro(seed)
        floats = [bits((y.next() >> 11) * (1.0 / 9007199254740992.0)) for _ in range(16)]
        cases.append({'seed': seed, 'ulongs': [str(u) for u in ulongs], 'floats': floats})
    return cases


def random_error_cases() -> list[dict]:
    cases = []
    for k in (4, -1):
        try:
            random.sample(range(3), k)
        except ValueError as e:
            cases.append({'op': 'sample', 'n': 3, 'k': k, **err(e)})
    for args in ((0,), (-4,), (5, 5), (7, 3)):
        try:
            random.randrange(*args)
        except ValueError as e:
            cases.append({'op': 'randrange', 'args': list(args), **err(e)})
    return cases


def build_values() -> dict:
    return {
        'cases': {
            'blake2b': blake2b_cases(),
            'blake2b_error': blake2b_error_cases(),
            'env': env_cases(),
            'lru': lru_cases(),
            'random_sequence': random_sequences(),
            'random_error': random_error_cases(),
        },
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=True) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_small_values: up to date')
        return 0
    VALUES_PATH.parent.mkdir(parents=True, exist_ok=True)
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
