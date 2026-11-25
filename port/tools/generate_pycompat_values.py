"""Records what real Python gives for each PyCompat helper, and the Unicode tables that the helpers use.

Two files come out of one run:

- tests/Graphiti.Net.Property.Tests/python-values.json: inputs and the Python result of each one.
  The property tests read it. A result is a value or an error name.
- src/Graphiti.Net.PyCompat/PyUnicodeData.g.cs: the character tables of the Python build that
  records the values (case mapping, case classes, decimal digits). Upstream runs on that build,
  and .NET holds another Unicode version, so the helpers read these tables and not .NET's.

    .upstream/graphiti/.venv/bin/python port/tools/generate_pycompat_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_pycompat_values.py --check

--check writes nothing and exits 1 if a file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.
Strings are lists of code points, because JSON text cannot carry a lone surrogate.
No case holds a high surrogate directly before a low surrogate: a .NET string would join them.
Floats are the 64 bits of the value, as a signed integer. Nothing here uses the network.
"""
from __future__ import annotations

import json
import math
import random
import struct
import sys
import unicodedata
import uuid
from itertools import combinations, zip_longest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.json'
TABLES_PATH = ROOT / 'src' / 'Graphiti.Net.PyCompat' / 'PyUnicodeData.g.cs'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_pycompat_values: needs Python 3.12, found {sys.version.split()[0]}')

LONG_MIN, LONG_MAX = -(2**63), 2**63 - 1
MAX_CP = 0x110000


def cps(s: str) -> list[int]:
    return [ord(c) for c in s]


def from_cps(values: list[int]) -> str:
    return ''.join(chr(v) for v in values)


def has_joinable_surrogates(s: str) -> bool:
    return any(0xD800 <= ord(a) <= 0xDBFF and 0xDC00 <= ord(b) <= 0xDFFF for a, b in zip(s, s[1:]))


def bits(x: float) -> int:
    return struct.unpack('<q', struct.pack('<d', x))[0]


def err_name(e: Exception) -> str:
    return type(e).__name__


def in_long(n: int) -> bool:
    return LONG_MIN <= n <= LONG_MAX


def int_result(n: int) -> dict:
    """Python's int result. One that a C# long cannot hold is recorded as it is, in 'big' (a decimal
    string). The helper throws OverflowException for it: the representation limit of debt T25."""
    return {'r': n} if in_long(n) else {'big': str(n)}


def msg(e: Exception) -> dict:
    return {'e': err_name(e), 'msg': cps(str(e))}


def R(thunk, conv=None) -> dict:
    """Runs a Python expression. Returns {'r': value}, or {'e': error class, 'msg': text}."""
    try:
        v = thunk()
        return conv(v) if conv else {'r': v}
    except (ZeroDivisionError, ValueError, OverflowError, TypeError, IndexError) as e:
        return msg(e)


def float_result(x: float) -> dict:
    return {'r': bits(x), 'nan': math.isnan(x)}


# ---------------------------------------------------------------------------------------------
# Unicode tables
# ---------------------------------------------------------------------------------------------

def ranges(predicate) -> list[int]:
    """Flat list [start, end, start, end, ...] of the code points where predicate is true."""
    out: list[int] = []
    start = None
    for c in range(MAX_CP):
        if predicate(c):
            if start is None:
                start = c
        elif start is not None:
            out += [start, c - 1]
            start = None
    if start is not None:
        out += [start, MAX_CP - 1]
    return out


def is_cased(c: int) -> bool:
    ch = chr(c)
    return ch.islower() or ch.isupper() or unicodedata.category(ch) == 'Lt'


def sigma_after(prefix: str, c: int) -> bool:
    """True if CPython lowers a sigma that follows prefix+c to the final form."""
    return (prefix + chr(c) + 'Σ').lower()[-1] == 'ς'


def is_case_ignorable(c: int) -> bool:
    # 'a' + c + sigma is final if c is cased or ignorable. '1' + c + sigma is final if c is cased
    # and not ignorable. The first without the second is exactly "ignorable".
    return sigma_after('a', c) and not sigma_after('1', c)


def digit_starts() -> list[int]:
    """First code point of each run of ten decimal digits. int() and float() read these."""
    starts = []
    for c in range(MAX_CP):
        ch = chr(c)
        if ch.isdecimal() and unicodedata.decimal(ch) == 0:
            for k in range(10):
                assert unicodedata.decimal(chr(c + k)) == k, hex(c)
            starts.append(c)
    count = sum(1 for c in range(MAX_CP) if chr(c).isdecimal())
    assert count == 10 * len(starts), 'a decimal digit run is not ten wide'
    return starts


def mapping(fn) -> list[tuple[int, str]]:
    return [(c, fn(chr(c))) for c in range(MAX_CP) if fn(chr(c)) != chr(c)]


SPACE = [0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x85, 0xA0, 0x1680,
         *range(0x2000, 0x200B), 0x2028, 0x2029, 0x202F, 0x205F, 0x3000]


def tables_source() -> str:
    assert [c for c in range(MAX_CP) if chr(c).isspace()] == SPACE, 'the whitespace set changed'
    assert [c for c in range(MAX_CP) if chr(c).isdecimal()] == [
        s + k for s in digit_starts() for k in range(10)]

    def int_array(name: str, values: list[int], doc: str) -> str:
        rows = ', '.join(f'0x{v:X}' for v in values)
        return (f'    /// <summary>{doc}</summary>\n'
                f'    internal static readonly int[] {name} = [{rows}];\n')

    def map_arrays(name: str, items: list[tuple[int, str]], doc: str) -> str:
        keys = ', '.join(f'0x{c:X}' for c, _ in items)
        values = ', '.join('"' + ''.join(f'\\u{ord(ch):04X}' if ord(ch) < 0x10000 else
                                         f'\\U{ord(ch):08X}' for ch in v) + '"' for _, v in items)
        return (f'    /// <summary>{doc}</summary>\n'
                f'    internal static readonly int[] {name}Keys = [{keys}];\n'
                f'    /// <summary>The result for each key of {name}Keys.</summary>\n'
                f'    internal static readonly string[] {name}Values = [{values}];\n')

    parts = [
        '// <auto-generated>\n'
        '// Written by port/tools/generate_pycompat_values.py. Do not edit.\n'
        f'// Python {sys.version.split()[0]}, Unicode {unicodedata.unidata_version}.\n'
        '// </auto-generated>\n'
        '#nullable enable\n\n'
        'namespace Graphiti.PyCompat;\n\n'
        '/// <summary>The character tables of the Python build that upstream runs on.</summary>\n'
        'internal static class PyUnicodeData\n{\n',
        int_array('Upper', ranges(lambda c: chr(c).isupper()),
                  'Ranges (first, last) of the code points that str.isupper() accepts.'),
        int_array('Lower', ranges(lambda c: chr(c).islower()),
                  'Ranges (first, last) of the code points that str.islower() accepts.'),
        int_array('Title', ranges(lambda c: unicodedata.category(chr(c)) == 'Lt'),
                  'Ranges (first, last) of the titlecase letters.'),
        int_array('Cased', ranges(is_cased), 'Ranges (first, last) of the cased code points.'),
        int_array('NonPrintable', ranges(lambda c: not chr(c).isprintable()),
                  'Ranges (first, last) of the code points that str.isprintable() rejects.'),
        int_array('CaseIgnorable', ranges(is_case_ignorable),
                  'Ranges (first, last) of the case-ignorable code points.'),
        int_array('DecimalDigitStarts', digit_starts(),
                  'The code point of the digit zero of each run of ten decimal digits.'),
        map_arrays('Lowercase', mapping(str.lower), 'The full lowercase mapping of one code point.'),
        map_arrays('Titlecase', mapping(str.title), 'The full titlecase mapping of one code point.'),
        '}\n',
    ]
    return '\n'.join(parts)


# ---------------------------------------------------------------------------------------------
# Cases
# ---------------------------------------------------------------------------------------------

SEED = 20261125

BASE_STRINGS = [
    '', 'a', 'abc', 'hello world', '  padded  ', 'MiXeD CaSe 123', 'ÀÉÎ', 'ÀÉÎ abc',
    '\U0001F600', 'a\U0001F600b', 'é', 'é', 'ño', '\U0001F468‍\U0001F469‍\U0001F467',
    '\U0001F1E6\U0001F1FA', 'ΑΣ', 'ΑΣΑ', 'Σ', 'ΑΣ.', 'a.Σ', 'ΑΣ́', 'ΑΣ́Α', '1Σ', 'Σ.Α', 'ὈΔΥΣΣΕΎΣ',
    'ß', 'ǆ', 'ǅ', 'Ǆ', 'İ', 'ı', 'ŉ', 'ﬁ', 'ǰ', 'ẞ', 'ΐ', 'ᾳ', 'ᾼ', 'ǈ', 'ʼn',
    '\U0001D400\U0001D41B', '\U00010428', '\U00010400', '\U00010D50', '\U00010D70',
    '\x00', '\x1c\x1d\x1e\x1f', ' ​ ', '\ud800', 'a\udc00b', 'x\udbffy',
    '\t\n\r\x0b\x0c\x1c\x1d\x1e\x1f\x20\x85\xa0       　',
    'a\r\nb\rc\nd\x0be\x0cf\x1cg\x1dh\x1ei\x85j k l', '\n\n', 'a\n', '\r\n\r\n',
    'a,b,,c', ',', 'a.b.c', 'x--y--z', 'abab', 'ababab', ' a b  c ', '.,!?;:\'"()[]{}word.,',
    'Alice', 'alice smith', 'ALICE', 'Hello World', '́', 'á́b', '٣٤', '￿', '',
    '\U00010000', '\U0010ffff', 'A\u01c5', '\u01c5A', 'Aa', 'a\u01c5', 'A\u01c8B',
]
assert not any(has_joinable_surrogates(s) for s in BASE_STRINGS)


def random_strings(rng: random.Random, count: int) -> list[str]:
    pools = [
        list(range(0x20, 0x7F)),
        list(range(0xA0, 0x180)),
        list(range(0x370, 0x400)),
        list(range(0x4E00, 0x4E40)),
        list(range(0x1F600, 0x1F640)),
        list(range(0x300, 0x370)),
        list(range(0x10400, 0x10450)),
        [0x20, 0x09, 0x0A, 0x85, 0xA0, 0x2028, 0x3000],
        [0xD800, 0xDBFF, 0xDC00, 0xDFFF],
        [0xFFFF, 0xE000, 0x10000, 0x10FFFF],
    ]
    out = []
    while len(out) < count:
        n = rng.randint(0, 12)
        s = ''.join(chr(rng.choice(rng.choice(pools))) for _ in range(n))
        if not has_joinable_surrogates(s):
            out.append(s)
    return out


def cap(rng: random.Random, items: list, n: int) -> list:
    return items if len(items) <= n else rng.sample(items, n)


def build_values() -> dict:
    rng = random.Random(SEED)
    strings = list(dict.fromkeys(BASE_STRINGS + random_strings(rng, 60)))
    sidx = {s: i for i, s in enumerate(strings)}
    cases: dict[str, list] = {}

    def add(name: str, case: dict) -> None:
        cases.setdefault(name, []).append(case)

    ints = [0, 1, -1, 2, -2, 3, -3, 7, -7, 10, -10, 100, 2**31 - 1, -2**31, 2**53, 2**53 + 1,
            -(2**53) - 1, 2**62, 2**63 - 1, -2**63, 12345678901234, -9007199254740993]
    combs = [(n, k) for n in range(-2, 13) for k in range(-2, 15)] + [
        (66, 33), (67, 33), (67, 1), (68, 34), (100, 50), (100, 3), (62, 31), (2**62, 1), (2**62, 2),
        (2**63 - 1, 0), (2**63 - 1, 1), (2**63 - 1, 2), (2**63 - 1, 2**63 - 1), (2**63 - 1, 2**63 - 2),
        (10**18, 3), (10**18, 1), (10**18, 0), (0, 0), (0, 1), (1, 0), (-2**63, 0), (5, -2**63), (21, 10), (64, 32)]
    for n, k in combs:
        add('comb', {'n': n, 'k': k, **R(lambda: math.comb(n, k), int_result)})
    floats = [0.0, -0.0, 1.0, -1.0, 0.5, -0.5, 2.5, -7.25, 3.0, 1e308, -1e308, 5e-324, 1e-300,
              math.inf, -math.inf, math.nan, 0.1, 1e18, 9.3e18, -9.3e18, 2.9, -2.9,
              2.0**63, -2.0**63, math.nextafter(2.0**63, 0.0), math.nextafter(-2.0**63, -math.inf)]

    for a in ints:
        for b in ints:
            add('true_div_int', {'a': a, 'b': b, **R(lambda: a / b, float_result)})
            add('floor_div', {'a': a, 'b': b, **R(lambda: a // b, int_result)})
            add('mod', {'a': a, 'b': b, **R(lambda: a % b, int_result)})

    for a in floats:
        for b in floats:
            add('true_div_float', {'a': bits(a), 'b': bits(b), **R(lambda: a / b, float_result)})

    for x in floats:
        add('int_float', {'x': bits(x), **R(lambda: int(x), int_result)})

    int_texts = [
        '0', '-0', '+5', '  42  ', '\t\n7\r', '1_000', '1__0', '_1', '1_', '', '+', '-', '12 3', '٣٤', '０１２',
        '00012', '1e3', '0x10', '9223372036854775807', '9223372036854775808', '-9223372036854775808',
        '-9223372036854775809', ' 7', '\x1c5\x1f', '١_٢', '1.0', '²', '٣\U0001F600', '\U0001F600',
        '5́', '+ 5', '- 5', '--5', '5-', '१२३', ' -12 ', '1_2_3', '0_0', '+_1', '1\x00',
        '\U00010D40', '\U0001D7CE\U0001D7CF', '\ud800', '\U00010035', '1\U00010035', '\U00010035\U0001D7CE',
        '1\U0001002E5', '5\U0001002E',
    ]
    # The error message cuts the repr after 200 code points (%.200R). Lengths around the cut, a
    # non-printable code point whose escape crosses it, and an astral letter on each side of it.
    int_texts += ['a' * n for n in (1, 196, 197, 198, 199, 200, 300)]
    int_texts += ['a' * 197 + '\x00', 'a' * 196 + '\x00', 'a' * 198 + '\U000e0001', 'b' * 195 + '\u200b' + 'c' * 10,
                  'a' * 198 + '\U0001F600', 'a' * 199 + '\U0001F600', '  ' + 'a' * 300, "'" + 'a' * 250,
                  "'" * 120 + '"', '\u00e9' * 260, '\ud800' * 120]
    int_texts += ['0' * 4300, '0' * 4301, '1' * 4301, '0_' * 4300 + '0', '-' + '0' * 4301, ' ' + '0' * 4301]
    for t in int_texts:
        add('int_str', {'s': cps(t), **R(lambda: int(t), int_result)})

    float_texts = [
        '0', '-0', '1.5', '  -2.5e3 ', '1_0.5_0', '1e999', '-1e999', 'inf', '-Infinity', '+nan', 'NaN', 'INF',
        'infinit', '', '.', '1.', '.5', '1e', 'e5', '1e+', '1_', '1__0', '0x1p3', '١٫٥', '٣.٥', '1e-400',
        '4.9e-324', '1.7976931348623157e308', '1.797693134862316e308', '3.14159265358979323846264338327950288',
        '0.1', '1_000.000_1e1_0', '\x1c1.5\x1f', '\x851.5\x85', ' 1 ', '1e5_0', '1_e5', '1e_5', '1._5', '1_.5', '-.5e-2',
        '+1.', 'in f', 'nan1', '- 1', '+-1', '1.5.5', '१.५', '5́', '\ud800', '1\U0001002E5', '1\U00010035', '9' * 400, '0.' + '0' * 400 + '1',
    ]
    for t in float_texts:
        add('float_str', {'s': cps(t), **R(lambda: float(t), float_result)})

    for s in strings:
        add('len', {'s': sidx[s], 'r': len(s)})
        add('code_points', {'s': sidx[s], 'r': cps(s)})
        add('lower', {'s': sidx[s], 'r': cps(s.lower())})
        add('capitalize', {'s': sidx[s], 'r': cps(s.capitalize())})
        add('is_upper', {'s': sidx[s], 'r': s.isupper()})
        add('strip', {'s': sidx[s], 'r': cps(s.strip())})
        add('lstrip', {'s': sidx[s], 'r': cps(s.lstrip())})
        add('rstrip', {'s': sidx[s], 'r': cps(s.rstrip())})
        add('split_ws', {'s': sidx[s], 'r': [cps(p) for p in s.split()]})
        add('splitlines', {'s': sidx[s], 'r': [cps(p) for p in s.splitlines()]})
        add('encode_utf8', {'s': sidx[s], **R(lambda: list(s.encode('utf-8')))})
        add('repr_str', {'s': sidx[s], 'r': cps(repr(s))})
        add('drop_lone_surrogates', {'s': sidx[s], 'r': cps(s.encode('utf-8', errors='ignore').decode('utf-8'))})
        add('ord', {'s': sidx[s], **R(lambda: ord(s))})
        for chars in ['', ' ', 'ab', '\U0001F600', '.,!?;:\'"()[]{}', '́', 'e', '\n\r', '/']:
            add('strip_chars', {'s': sidx[s], 'chars': cps(chars), 'r': cps(s.strip(chars))})
            add('rstrip_chars', {'s': sidx[s], 'chars': cps(chars), 'r': cps(s.rstrip(chars))})
        for sep in ['', ',', '.', '--', 'ab', '\U0001F600', ' ', '\n', 'Σ', '́', '\ud83d', '\ude00']:
            for n in [-1, 0, 1, 2, 5]:
                add('split_sep', {'s': sidx[s], 'sep': cps(sep), 'n': n,
                                  **R(lambda: [cps(p) for p in s.split(sep, n)])})

    index_opts = [None, 0, 1, 2, -1, -2, 5, 100, -100, 2**63 - 1, -(2**63)]
    step_opts = [None, 1, 2, 3, -1, -2, 0, 2**63 - 1, -(2**63)]
    at_idx = [0, 1, 2, -1, -2, -3, 5, -5, 100, -100]
    for s in strings:
        for i in at_idx:
            add('at', {'s': sidx[s], 'i': i, **R(lambda: cps(s[i]))})
    slice_strings = [s for s in strings if len(s) <= 6][:40]
    product = [(s, a, b, st) for s in slice_strings for a in index_opts for b in index_opts for st in step_opts]
    for s, a, b, st in cap(rng, product, 6000):
        add('slice_str', {'s': sidx[s], 'a': a, 'b': b, 'step': st, **R(lambda: cps(s[a:b:st]))})
    int_lists = [[], [1], [1, 2], [5, 4, 3, 2, 1], list(range(10))]
    for xs in int_lists:
        for a in index_opts:
            for b in index_opts:
                for st in step_opts:
                    add('slice_list', {'xs': xs, 'a': a, 'b': b, 'step': st, **R(lambda: xs[a:b:st])})

    subs = ['', 'a', ' ', 'ab', '\U0001F600', 'b', 'e', '́', '.', 'Σ']
    find_opts = [None, 0, 1, 2, -1, -3, 100, -100]
    product = [(s, sub, a, b) for s in strings for sub in subs for a in find_opts for b in find_opts]
    for s, sub, a, b in cap(rng, product, 6000):
        add('find', {'s': sidx[s], 'sub': cps(sub), 'a': a, 'b': b, 'r': s.find(sub, a, b) if a is not None or b is not None
                     else s.find(sub)})
        add('rfind', {'s': sidx[s], 'sub': cps(sub), 'a': a, 'b': b, 'r': s.rfind(sub, a, b) if a is not None or b is not None
                      else s.rfind(sub)})

    pairs = [(a, b) for a in strings for b in strings]
    for a, b in cap(rng, pairs, 4000) + [('￿', '\U00010000'), ('', '\U0001F600'),
                                          ('\ud800', ''), ('\U00010000', '\ud800'), ('a', 'a'), ('', ''), ('', 'a')]:
        add('compare_strings', {'a': cps(a), 'b': cps(b), 'r': (a > b) - (a < b)})

    byte_samples = [
        b'', b'abc', 'é'.encode(), '€'.encode(), '\U0001F600'.encode(), b'\xef\xbb\xbf', b'\xef\xbf\xbd',
        b'\x80', b'\xff', b'\xc0', b'\xc1', b'\xf5', b'\xc0\x80', b'\xe0\x80\x80', b'\xe0\x9f\xbf', b'\xed\xa0\x80',
        b'\xed\xbf\xbf', b'\xf4\x90\x80\x80', b'\xf0\x80\x80\x80', b'\xf0\x8f\xbf\xbf', b'\xe2\x82', b'\xe2',
        b'\xf0\x9f\x98', b'\xf0\x9f', b'a\xe2\x82', b'a\xe2\x82b', b'\xe2\x28\xa1', b'\xf0\x28\x8c\xbc',
        b'\xf0\x90\x28\xbc', b'\xf0\x28\x8c\x28', b'\xc3', b'\xc3(', b'\xc3\xa9\xc3', b'ab\x80cd', b'\x80\x80\x80',
        b'\xf8\x88\x80\x80\x80', b'\xfc\x84\x80\x80\x80\x80', b'\xf4\x8f\xbf\xbf', b'\xf4\x8f\xbf', b'\xed\x9f\xbf',
        b'\xee\x80\x80', b'\xef\xbf\xbf', b'\xf4\x90', b'\xf4\x90\x80\x80', b'\xe0\x80', b'\xe0\xa0', b'\xed\xa0', b'\xf0\x80', b'\xf0\x90\x80', b'\xc2', b'\xc2\x20', b'x\xc3', b'\xe2\x82\xff', b'\xf0\x9f\x98\xff', b'\xff\xfe', b'\xc0\xaf', b'\xf1\x80\x80\x80', b'\xf3\xbf\xbf\xbf',
    ]
    for _ in range(1500):
        n = rng.randint(0, 10)
        byte_samples.append(bytes(rng.choice([rng.randint(0, 255), rng.randint(0x80, 0xBF), rng.randint(0xC0, 0xF4)])
                                  for _ in range(n)))
    for b in byte_samples:
        for errors in ['strict', 'ignore']:
            add('decode_utf8', {'b': list(b), 'errors': errors, **R(lambda: cps(b.decode('utf-8', errors=errors)))})

    for n in range(0, 7):
        for k in range(-1, 8):
            xs = list(range(10, 10 + n))
            add('combinations', {'xs': xs, 'k': k, **R(lambda: [list(c) for c in combinations(xs, k)])})

    lists = [[], [1], [1, 2, 3], [0, 0], [4, 5, 6, 7, 8]]
    for a in lists:
        for b in lists:
            add('zip_longest', {'a': a, 'b': b, 'r': [list(p) for p in zip_longest(a, b)]})

    names = [s for s in strings if not any(0xD800 <= ord(c) <= 0xDFFF for c in s)]
    names += ['graphiti:ingest:group-1:unit-1:0', 'graphiti:ingest::u:12', 'https://example.com/a?b=c']
    for s in names:
        add('uuid5_url', {'name': cps(s), 'r': str(uuid.uuid5(uuid.NAMESPACE_URL, s))})
    for s in ['\ud800', 'a\udc00']:
        add('uuid5_url', {'name': cps(s), **R(lambda: str(uuid.uuid5(uuid.NAMESPACE_URL, s)))})

    # Single code points for the table checks: every range boundary, and a seeded sample.
    probes = sorted(set([0, 0x1F, 0x20, 0x7F, 0x80, 0x85, 0xA0, 0xFF, 0x100, 0x2000, 0x200B, 0x3000, 0xD7FF, 0xD800,
                         0xDFFF, 0xE000, 0xFFFF, 0x10000, 0x10400, 0x10D50, 0x10D70, 0x1D400, 0x1F600, 0x10FFFF] +
                        [rng.randrange(0, MAX_CP) for _ in range(1500)] +
                        [rng.randrange(0x370, 0x600) for _ in range(300)] + [rng.randrange(0x1C00, 0x1F00) for _ in range(150)] +
                        [0x1E9E, 0x130, 0x131, 0x149, 0x1C4, 0x1C5, 0x1C6, 0x1F0, 0x390, 0x1FB3, 0xFB01]))
    tables = {'Upper': ranges(lambda c: chr(c).isupper()), 'Lower': ranges(lambda c: chr(c).islower()),
              'Title': ranges(lambda c: unicodedata.category(chr(c)) == 'Lt'), 'Cased': ranges(is_cased),
              'NonPrintable': ranges(lambda c: not chr(c).isprintable()),
              'CaseIgnorable': ranges(is_case_ignorable)}
    edge = set()
    for flat in tables.values():
        for v in flat:
            edge.update(x for x in (v - 1, v, v + 1) if 0 <= x < MAX_CP)
    for v in digit_starts():
        edge.update(x for x in (v - 1, v, v + 9, v + 10) if 0 <= x < MAX_CP)
    edge.update(c for c, _ in mapping(str.lower))
    edge.update(c for c, _ in mapping(str.title))
    probes = sorted(set(probes) | edge)
    for c in probes:
        ch = chr(c)
        add('code_point_props', {'c': c, 'upper': ch.isupper(), 'lower': ch.islower(), 'space': ch.isspace(),
                                 'decimal': ch.isdecimal(), 'digit': int(ch) if ch.isdecimal() else None,
                                 'a_upper': ('A' + ch).isupper(), 'printable': ch.isprintable(), 'lower_map': cps(ch.lower()), 'title_map': cps(ch.title())})

    return {
        'meta': {'python': sys.version.split()[0], 'unicode': unicodedata.unidata_version, 'seed': SEED,
                 'note': 'Generated by port/tools/generate_pycompat_values.py. Do not edit.'},
        'strings': [cps(s) for s in strings],
        'cases': cases,
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    values_text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=True) + '\n'
    tables_text = tables_source()
    stale = []
    for path, text in ((VALUES_PATH, values_text), (TABLES_PATH, tables_text)):
        if check:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                stale.append(path)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding='utf-8', newline='\n')
            print(f'wrote {path.relative_to(ROOT)} ({len(text)} bytes)')
    if stale:
        for path in stale:
            print(f'stale: {path.relative_to(ROOT)}')
        return 1
    if check:
        print('generate_pycompat_values: up to date')
    return 0


if __name__ == '__main__':
    sys.exit(main())
