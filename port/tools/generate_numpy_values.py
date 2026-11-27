"""Records what numpy gives for the kernels that PyNumpy ports.

Two files come out of one run: tests/Graphiti.Net.Property.Tests/python-values.numpy.json, and
python-values.numpy-float32.json in the same folder for the float32 kernels (each file stays under 1,572,864 bytes).

    .upstream/graphiti/.venv/bin/python port/tools/generate_numpy_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_numpy_values.py --check

--check writes nothing and exits 1 if a file differs from what the run would write.
The tool needs Python 3.12 and the numpy of the pinned upstream environment.
Floats are the 64 bits of the value, as a signed integer.
'add_reduce' is np.add.reduce of a contiguous float64 array, which numpy sums in pairs (RULES R18).
'add_reduce_float32' is the same for a float32 array, which numpy sums in pairs in float32. A float32 value is the
32 bits of the value as 8 hex digits, and an array is one string of them.
'argsort' is np.argsort of a float32 array with the default kind 'quicksort', which is not stable: numpy 2.4.1 of
macOS arm64 runs the generic introsort of npysort/quicksort.cpp (median of three, insertion sort for 16 items or
fewer, heapsort past the depth limit, nan last). An array of the random cases is a string of indexes into the one
'palette' of float32 values (one character of PALETTE_DIGITS each), and its result is 'sha', the first 16 hex digits
of the SHA-256 of the indexes of the result joined by ','. The other cases give the input and the result in full.
Nothing here uses the network.
"""
from __future__ import annotations

import hashlib
import json
import random
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.numpy.json'
FLOAT32_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.numpy-float32.json'
SIZE_LIMIT = 1_572_864
PALETTE_DIGITS = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_numpy_values: needs Python 3.12, found {sys.version.split()[0]}')

LENGTHS = [
    *range(0, 18), 23, 24, 25, 63, 64, 65, 120, 127, 128, 129, 130, 136, 137, 255, 256, 257,
    384, 511, 512, 513, 768, 1000, 1024, 1025, 1536, 2049,
]  # fmt: skip


def bits(x: float) -> int:
    return struct.unpack('<q', struct.pack('<d', x))[0]


def add_reduce_cases() -> list[dict]:
    rng = random.Random(20261127)
    cases = []
    for n in LENGTHS:
        for scale in (1.0, 1e-9, 1e9):
            values = [rng.uniform(-1.0, 1.0) * scale for _ in range(n)]
            if scale == 1e9 and n > 2:
                # Mixed sizes make the order of the additions show in the last bits.
                values[rng.randrange(n)] = 1e-9
                values[rng.randrange(n)] = -1e17
            cases.append(values)
    cases.append([0.0] * 9)
    cases.append([-0.0] * 3)
    cases.append([-0.0] * 9)
    cases.append([-0.0] * 200)
    cases.append([1.0, float('inf'), 2.0])
    cases.append([float('inf'), float('-inf')])
    cases.append([1.0, float('nan'), 2.0] * 5)
    cases.append([1e308] * 9)
    return [
        {'values': [bits(v) for v in values], 'sum': bits(float(np.add.reduce(np.array(values, dtype=np.float64))))}
        for values in cases
    ]


def hex32(values) -> str:
    return np.asarray(values, dtype=np.float32).astype('>f4').tobytes().hex()


def sha(order) -> str:
    return hashlib.sha256(','.join(str(i) for i in order).encode('ascii')).hexdigest()[:16]


# The widths of review A of packet P8b: below 8, around 128, the splits above 128, and real embedding widths.
FLOAT32_WIDTHS = [*range(0, 10), 127, 128, 129, 130, 255, 256, 257, 383, 384, 385, 512, 768, 1023, 1024, 1025, 1536, 3072]


def add_reduce_float32_cases() -> list[dict]:
    rng = random.Random(20261128)
    kinds = [
        lambda: rng.uniform(-1.0, 1.0),
        lambda: rng.uniform(0.0, 1.0) * 10.0 ** rng.randint(-3, 3),
        lambda: rng.gauss(0.0, 0.05),
        lambda: 1.0 + rng.uniform(-1e-3, 1e-3),
        lambda: rng.uniform(0.0, 1.0) ** 2,
    ]
    cases = []
    for n in FLOAT32_WIDTHS:
        for kind in rng.sample(kinds, 3):
            cases.append([kind() for _ in range(n)])
    cases.append([0.0] * 300)
    cases.append([-0.0] * 3)
    cases.append([-0.0] * 300)
    cases.append([1e30] * 130)
    cases.append([3.4e38, 3.4e38])
    cases.append([1e20] * 130)
    cases.append([1e-45] * 9)
    cases.append([1e-40] * 257)
    cases.append([1.0, float('inf'), 2.0] * 50)
    cases.append([float('inf'), float('-inf')])
    cases.append([1.0, float('nan'), 2.0] * 5)
    out = []
    for values in cases:
        array = np.asarray(values, dtype=np.float32)
        out.append({'values': hex32(array), 'sum': hex32([np.add.reduce(array)])})
    return out


def argsort_cases() -> dict:
    rng = random.Random(20261129)
    specials = [0.0, -0.0, 1.0, -1.0, 0.5, -0.5, float('nan'), -float('nan'), float('inf'), float('-inf'), 1e-45,
                -1e-45, 3.4028234663852886e38, -3.4028234663852886e38, 0.6, 0.6000000238418579]
    palette = np.asarray(specials + [rng.uniform(-1.0, 1.0) for _ in range(64 - len(specials))], dtype=np.float32)
    finite = [i for i in range(len(palette)) if np.isfinite(palette[i])]
    random_cases = []
    for _ in range(3000):
        n = rng.randint(0, 300)
        source = rng.choice([finite, finite, list(range(len(palette)))])
        chosen = rng.sample(source, rng.choice([1, 2, 3, 5, 8, 16, 32, len(source)]))
        indexes = [rng.choice(chosen) for _ in range(n)]
        array = palette[indexes]
        random_cases.append({'values': ''.join(PALETTE_DIGITS[i] for i in indexes), 'sha': sha(np.argsort(array).tolist())})
    full = []

    def add(values):
        array = np.asarray(values, dtype=np.float32)
        full.append({'values': hex32(array), 'order': np.argsort(array).tolist()})

    for n in (0, 1, 2, 16, 17, 18, 20, 33, 100):
        add([1.0] * n)
    add([-1.0] * 17)
    add([float('nan')] * 20)
    add([float('nan'), 1.0, float('nan'), -0.0, 0.0, float('inf'), -float('inf'), 2.0] * 3)
    add([-0.0, 0.0] * 12)
    add([float('inf'), float('nan'), 3.0] * 7)
    add([rng.uniform(-1.0, 1.0) for _ in range(300)])
    add([rng.choice([rng.uniform(-1.0, 1.0), float('nan')]) for _ in range(257)])
    # Inputs that reach the heapsort: a McIlroy adversary against this introsort, with values of adjacent rank made
    # equal where the partitions before the heapsort never compare them. So the partitions run as for the adversary,
    # and the heapsort orders equal values.
    add(HEAPSORT_40)
    add(HEAPSORT_64)
    add([-x for x in HEAPSORT_64])
    return {'palette': hex32(palette), 'random': random_cases, 'full': full}


HEAPSORT_40 = [0, 20, 2, 22, 4, 38, 6, 36, 8, 38, 10, 36, 12, 34, 14, 34, 16, 32, 18, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19,
               21, 30, 30, 28, 28, 26, 26, 24, 24, 32, 22]
HEAPSORT_64 = [0, 56, 2, 56, 4, 54, 6, 54, 8, 52, 10, 52, 12, 50, 14, 50, 16, 48, 18, 48, 20, 46, 22, 46, 24, 26, 62, 60,
               62, 60, 58, 1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 44, 44, 42, 42, 40, 40, 38, 38, 36, 36, 34, 34,
               32, 32, 30, 30, 28, 28, 58, 26]


def build_values() -> dict:
    return {'numpy': np.__version__, 'cases': {'add_reduce': add_reduce_cases()}}


def build_float32_values() -> dict:
    return {'numpy': np.__version__, 'palette_digits': PALETTE_DIGITS,
            'cases': {'add_reduce_float32': add_reduce_float32_cases(), 'argsort': argsort_cases()}}


def main() -> int:
    check = '--check' in sys.argv[1:]
    stale = False
    for path, values in ((VALUES_PATH, build_values()), (FLOAT32_PATH, build_float32_values())):
        text = json.dumps(values, separators=(',', ':'), ensure_ascii=True, allow_nan=False) + '\n'
        if len(text) > SIZE_LIMIT:
            sys.exit(f'{path.name} would have {len(text)} bytes, above the limit of {SIZE_LIMIT}')
        if check:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                print(f'stale: {path.relative_to(ROOT)}')
                stale = True
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8', newline='\n')
        print(f'wrote {path.relative_to(ROOT)} ({len(text)} bytes)')
    if check:
        if stale:
            return 1
        print('generate_numpy_values: up to date')
    return 0


if __name__ == '__main__':
    sys.exit(main())
