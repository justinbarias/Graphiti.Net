"""Records what numpy gives for the kernels that PyNumpy ports.

One file comes out of one run: tests/Graphiti.Net.Property.Tests/python-values.numpy.json.

    .upstream/graphiti/.venv/bin/python port/tools/generate_numpy_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_numpy_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12 and the numpy of the pinned upstream environment.
Floats are the 64 bits of the value, as a signed integer.
'add_reduce' is np.add.reduce of a contiguous float64 array, which numpy sums in pairs (RULES R18).
Nothing here uses the network.
"""
from __future__ import annotations

import json
import random
import struct
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.numpy.json'

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


def build_values() -> dict:
    return {'numpy': np.__version__, 'cases': {'add_reduce': add_reduce_cases()}}


def main() -> int:
    check = '--check' in sys.argv[1:]
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=True) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_numpy_values: up to date')
        return 0
    VALUES_PATH.parent.mkdir(parents=True, exist_ok=True)
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
