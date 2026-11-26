"""Records what real Python gives for PyRegex and for two PyText members, and the class tables of PyRegex.

Two files come out of one run:

- tests/Graphiti.Net.Property.Tests/python-values.regex.json: the answer of Python 3.12 `re` for
  each code point from U+0000 to U+10FFFF (`\\s`, `\\w`, `\\d`, and `\\b` in probe strings),
  the results of `re.split`, the `re.search` results of the three `re.IGNORECASE` patterns of
  temporal_edge_utils.py, the `$` cases of GAPS F7, and the cases of `str.replace` with a count
  and `str.count(sub)`. The property tests read it.
- src/Graphiti.Net.PyCompat/PyRegexData.g.cs: the character sets of `\\s`, `\\w`, and `\\d` of the Python
  build, as pieces of .NET pattern text. A .NET pattern holds UTF-16 units, so a set above U+FFFF
  becomes an alternation of surrogate pairs. PyRegex puts the pieces together.

    .upstream/graphiti/.venv/bin/python port/tools/generate_regex_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_regex_values.py --check

--check writes nothing and exits 1 if a file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.
A text is a JSON string. A text with a lone surrogate is a list of code points, because JSON text cannot carry it.
No text holds a high surrogate directly before a low surrogate: a .NET string would join them.
Nothing here uses the network. The run takes about half a minute.
"""
from __future__ import annotations

import json
import random
import re
import sys
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.regex.json'
DATA_PATH = ROOT / 'src' / 'Graphiti.Net.PyCompat' / 'PyRegexData.g.cs'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_regex_values: needs Python 3.12, found {sys.version.split()[0]}')

MAX_CP = 0x110000


def cps(s: str) -> list[int]:
    return [ord(c) for c in s]


def enc(s: str) -> str | list[int]:
    """A text as a JSON string, or as code points if it holds a lone surrogate, which JSON text cannot carry."""
    return cps(s) if any('\ud800' <= c <= '\udfff' for c in s) else s


def joinable(s: str) -> bool:
    """True if a high surrogate stands directly before a low one, which a .NET string joins."""
    return any('\ud800' <= a <= '\udbff' and '\udc00' <= b <= '\udfff' for a, b in zip(s, s[1:]))


# ---- the character classes, one code point at a time ----

CLASS_PATTERNS = {
    'space': re.compile(r'\s'),
    'word': re.compile(r'\w'),
    'digit': re.compile(r'\d'),
}


def to_ranges(flags: bytearray) -> list[int]:
    """Flat list a0, b0, a1, b1, ... of the inclusive ranges of the set bytes."""
    out: list[int] = []
    start = -1
    for cp in range(MAX_CP):
        if flags[cp]:
            if start < 0:
                start = cp
        elif start >= 0:
            out += [start, cp - 1]
            start = -1
    if start >= 0:
        out += [start, MAX_CP - 1]
    return out


def class_flags() -> dict[str, bytearray]:
    flags = {name: bytearray(MAX_CP) for name in CLASS_PATTERNS}
    for cp in range(MAX_CP):
        ch = chr(cp)
        for name, pattern in CLASS_PATTERNS.items():
            if pattern.fullmatch(ch) is not None:
                flags[name][cp] = 1
    # Cross checks of the oracle itself: these are the definitions of CPython 3.12.
    for cp in range(MAX_CP):
        ch = chr(cp)
        assert bool(flags['space'][cp]) == ch.isspace(), hex(cp)
        assert bool(flags['word'][cp]) == (ch.isalnum() or ch == '_'), hex(cp)
        assert bool(flags['digit'][cp]) == ch.isdecimal(), hex(cp)
    assert not any(flags['space'][0x10000:]), 'a white space character above U+FFFF'
    assert not any(flags['word'][0xD800:0xE000]), 'a surrogate matches \\w'
    return flags


# Probe strings for \b. "C" stands for the code point under test.
BOUNDARY_PROBES = [['a', 'C'], ['C', 'a'], [' ', 'C'], ['C', ' '], ['a', 'C', 'a'], [' ', 'C', ' '], ['C']]
BOUNDARY = re.compile(r'\b')


def boundary_runs() -> list:
    """Runs [first, last, masks] of code points that give the same boundary positions in every probe.

    Bit k of a mask is set if \\b matches at code point position k of the probe string.
    """
    runs: list = []
    current: list | None = None
    for cp in range(MAX_CP):
        ch = chr(cp)
        masks = []
        for probe in BOUNDARY_PROBES:
            text = ''.join(ch if t == 'C' else t for t in probe)
            mask = 0
            for m in BOUNDARY.finditer(text):
                mask |= 1 << m.start()
            masks.append(mask)
        if current is not None and current[2] == masks and current[1] == cp - 1:
            current[1] = cp
        else:
            current = [cp, cp, masks]
            runs.append(current)
    return runs


# ---- pattern text for .NET ----

def esc(cp: int) -> str:
    return f'\\u{cp:04X}'


def class_body(ranges: list[int]) -> str:
    out = []
    for a, b in zip(ranges[0::2], ranges[1::2]):
        out.append(esc(a) if a == b else f'{esc(a)}-{esc(b)}')
    return ''.join(out)


def bmp_part(ranges: list[int]) -> list[int]:
    out = []
    for a, b in zip(ranges[0::2], ranges[1::2]):
        if a >= 0x10000:
            continue
        out += [a, min(b, 0xFFFF)]
    return out


def astral_part(ranges: list[int]) -> list[tuple[int, int]]:
    return [(max(a, 0x10000), b) for a, b in zip(ranges[0::2], ranges[1::2]) if b >= 0x10000]


def astral_branches(ranges: list[tuple[int, int]]) -> list[str]:
    """The branches of an alternation that matches each code point of the ranges as a surrogate pair."""
    by_high: dict[int, list[list[int]]] = {}
    for a, b in ranges:
        for cp in (a, b):
            assert 0x10000 <= cp < MAX_CP
        cp = a
        while cp <= b:
            high = 0xD800 + ((cp - 0x10000) >> 10)
            low = 0xDC00 + ((cp - 0x10000) & 0x3FF)
            last_in_block = min(b, (((cp - 0x10000) >> 10) << 10) + 0x10000 + 0x3FF)
            low_end = 0xDC00 + ((last_in_block - 0x10000) & 0x3FF)
            lows = by_high.setdefault(high, [])
            if lows and lows[-1][1] + 1 == low:
                lows[-1][1] = low_end
            else:
                lows.append([low, low_end])
            cp = last_in_block + 1
    # Join neighbouring high surrogates that take the same low surrogates.
    groups: list[list] = []
    for high in sorted(by_high):
        lows = [tuple(r) for r in by_high[high]]
        if groups and groups[-1][1] + 1 == high and groups[-1][2] == lows:
            groups[-1][1] = high
        else:
            groups.append([high, high, lows])
    branches = []
    for first, last, lows in groups:
        high_text = esc(first) if first == last else f'[{esc(first)}-{esc(last)}]'
        low_text = '[' + ''.join(esc(a) if a == b else f'{esc(a)}-{esc(b)}' for a, b in lows) + ']'
        branches.append(high_text + low_text)
    return branches


def check_astral_branches(ranges: list[tuple[int, int]], branches: list[str]) -> None:
    """Match each astral code point as UTF-16 text against the alternation, with Python's own engine."""
    alt = re.compile('(?:' + '|'.join(branches) + ')')
    wanted = set()
    for a, b in ranges:
        wanted.update(range(a, b + 1))
    for cp in range(0x10000, MAX_CP):
        pair = chr(0xD800 + ((cp - 0x10000) >> 10)) + chr(0xDC00 + ((cp - 0x10000) & 0x3FF))
        assert (alt.fullmatch(pair) is not None) == (cp in wanted), hex(cp)


def chunked(tokens: list[str], sep: str, width: int = 96) -> str:
    """Verbatim C# string literals joined with +, with a line break after about `width` characters."""
    lines: list[str] = []
    current = ''
    for i, token in enumerate(tokens):
        piece = token + (sep if i < len(tokens) - 1 else '')
        if current and len(current) + len(piece) > width:
            lines.append(current)
            current = ''
        current += piece
    if current or not lines:
        lines.append(current)
    return ' +\n        '.join('@"' + line + '"' for line in lines)


def data_source(flags: dict[str, bytearray]) -> str:
    version = sys.version.split()[0]
    parts = [
        '// <auto-generated>\n'
        '// Written by port/tools/generate_regex_values.py. Do not edit.\n'
        f'// Python {version}, Unicode {unicodedata.unidata_version}.\n'
        '// </auto-generated>\n'
        '#nullable enable\n\n'
        'namespace Graphiti.PyCompat;\n\n'
        '/// <summary>\n'
        "/// Pieces of .NET pattern text for the character classes of Python's <c>re</c>. PyRegex puts them together.\n"
        '/// A body is the text for the inside of a character class. An alternation matches the code points above U+FFFF as surrogate pairs.\n'
        '/// </summary>\n'
        'internal static class PyRegexData\n{\n'
    ]

    def const(name: str, doc: str, tokens: list[str], sep: str) -> str:
        return (f'    /// <summary>{doc}</summary>\n'
                f'    internal const string {name} =\n        {chunked(tokens, sep)};\n\n')

    def body_tokens(ranges: list[int]) -> list[str]:
        return [esc(a) if a == b else f'{esc(a)}-{esc(b)}'
                for a, b in zip(ranges[0::2], ranges[1::2])]

    space = to_ranges(flags['space'])
    word = to_ranges(flags['word'])
    digit = to_ranges(flags['digit'])
    parts.append(const('SpaceBody', 'The inside of a character class for each code point that <c>\\s</c> matches. All are in the BMP.',
                       body_tokens(space), ''))
    for name, ranges, label in (('Word', word, '\\w'), ('Digit', digit, '\\d')):
        bmp = bmp_part(ranges)
        astral = astral_part(ranges)
        branches = astral_branches(astral)
        check_astral_branches(astral, branches)
        parts.append(const(f'{name}BmpBody', f'The inside of a character class for each code point of <c>{label}</c> up to U+FFFF.',
                           body_tokens(bmp), ''))
        parts.append(const(f'{name}Astral', f'An alternation of surrogate pairs for each code point of <c>{label}</c> above U+FFFF.',
                           ['(?:'] + [b + ('|' if i < len(branches) - 1 else '') for i, b in enumerate(branches)] + [')'], ''))
    parts.append('}\n')
    return ''.join(parts)


# ---- re.split ----

# name -> (Python pattern, flags). The test holds the hand translation of each one.
SPLIT_PATTERNS: dict[str, tuple[str, int]] = {
    # content_chunking.py:403
    'paragraph': (r'\n\s*\n', 0),
    # content_chunking.py:460, used by :461
    'sentence': (r'(?<=[.!?])\s+', 0),
    # content_chunking.py:588, searched at :589 with both flags (here also used to split)
    'speaker_search': (r'^([A-Za-z_][A-Za-z0-9_\s]*):(.+?)(?=^[A-Za-z_][A-Za-z0-9_\s]*:|$)', re.MULTILINE | re.DOTALL),
    # content_chunking.py:614, used by :615
    'speaker_split': (r'(?=^[A-Za-z_][A-Za-z0-9_\s]*:)', re.MULTILINE),
    'comma_group': (r'(,)', 0),
    'optional_groups': (r'(a)|(b)', 0),
    'two_groups': (r'(a)(b)?', 0),
    'star_x': (r'x*', 0),
    'empty': (r'', 0),
    'lazy_a': (r'a*?', 0),
    'verbose': (r'x*  # a comment at the end', re.VERBOSE),
    'empty_first': (r'|a', 0),
    'boundary': (r'\b', 0),
    'lookbehind_comma': (r'(?<=,)\s*', 0),
    'digits': (r'(\d+)', 0),
    'word_runs': (r'(\w+)', 0),
    # An empty match next to a character above U+FFFF. A .NET pattern must not match between the two units.
    'word_gap': (r'(?<!\w)(?!\w)', 0),
    'no_word_ahead': (r'(?!\w)', 0),
    'before_emoji': (r'(?=\U0001F600)', 0),
    'after_emoji': (r'(?<=\U0001F600)', 0),
}
# The four patterns of content_chunking.py get at least 200 texts each.
CHUNKING = ['paragraph', 'sentence', 'speaker_search', 'speaker_split']

COMMON_TOKENS = [
    'a', 'b', 'x', 'ab', 'Alice', ' ', '  ', '\n', '\n\n', '\r\n', '\t', ',', '.', '!', '?', ':', '_', '-',
    '1', '42', '٣', '\U0001D7D8',  # Arabic-Indic digit three, mathematical double-struck digit zero
    '\U0001F600', 'é', 'é', ' ', ' ', '\x1c', '　', '\u0085', 'ſ', '\ud800', '\udc00',
    '\U0001D400',  # mathematical bold capital A: a letter above U+FFFF
    '\U0001F4A9',
]
EXTRA_TOKENS = {
    'paragraph': ['\n\n', '\n \n', '\n \n', '\n\x1c\n', 'para', 'text'],
    'sentence': ['. ', '! ', '? ', '.\n', '.  ', 'Hi', 'it.', 'ok?'],
    'speaker_search': ['Alice:', 'Bob_2: ', '\nCarol: hi', '\nx y:', ': ', 'hello', '\n'],
    'speaker_split': ['Alice:', 'Bob_2: ', '\nCarol: hi', '\nx y:', ': ', 'hello', '\n'],
    'comma_group': [',', ',,', 'a,b'],
    'optional_groups': ['a', 'b', 'ab', 'ba', 'c'],
    'two_groups': ['a', 'b', 'ab', 'aab'],
    'star_x': ['x', 'xx', 'axb'],
    'empty': [],
    'lazy_a': ['a', 'aa', 'ab'],
    'verbose': ['x', 'xx', 'axb'],
    'empty_first': ['a', 'aa', 'b'],
    'boundary': ['ab ', ' cd', 'a-b', '\U0001D400 '],
    'lookbehind_comma': [', ', ',  ', ',a', 'x,'],
    'digits': ['12', '٣٤', '\U0001D7D8\U0001D7D9', 'a1'],
    'word_runs': ['ab', 'cd', '\U0001D400', 'é', '_'],
    'word_gap': ['a', ' ', '\U0001F600', '\U0001D400', 'ab'],
    'no_word_ahead': ['a', ' ', '\U0001F600', '\U0001D400'],
    'before_emoji': ['\U0001F600', 'a'],
    'after_emoji': ['\U0001F600', 'a'],
}
CURATED_TEXTS = [
    ' \U0001F600 ', 'a\U0001F600 ', 'ab\U0001F600\U0001F600c', '\U0001F600a',
    '', ' ', '\n', '\n\n', 'a', 'x', 'ab', 'abc', 'axb', 'a\n\nb', 'a\n \nb', 'a\n\n\nb', '\n\nabc', 'abc\n\n',
    'a\r\n\r\nb', 'a\n \nb', 'a\n\x1c\nb', 'a\n \nb', 'a. b', 'a.  b', 'a.\nb', 'a! b? c.', 'a.b', '. ', '.',
    'Alice: hi\nBob: yo', 'Alice: hi\nBob: yo\n', 'hello\nAlice: hi', 'Alice:', ':', 'Alice:\nBob:', 'A B:x\nC_1: y z',
    '\nAlice: hi', 'a,b,c', ',', ',,', 'a, b', 'a, b', 'ab', 'ba', 'aab', 'abab', 'xx', 'x', 'axxb', 'aaa',
    '\U0001F600', 'a\U0001F600b', '\U0001F600\U0001F600', 'é', 'a é', '\ud800', 'a\ud800b', '\udc00',
    '\udc00a\ud800', 'a\ud800 b', '٣', '1٣\U0001D7D8x', 'hi there', 'it\'s', '\U0001D400b \U0001D400',
    'aſb', ' ', 'a b',
]


def make_texts(name: str, count: int) -> list[str]:
    rng = random.Random(f'regex-split-{name}')
    seen: dict[str, None] = {}
    for text in CURATED_TEXTS:
        seen[text] = None
    tokens = COMMON_TOKENS + EXTRA_TOKENS[name] * 3
    while len(seen) < count:
        length = rng.choice([1, 2, 3, 4, 5, 6, 8, 10, 14, 20])
        text = ''.join(rng.choice(tokens) for _ in range(length))
        if not joinable(text):
            seen[text] = None
    return list(seen)


def split_value(item: str | None) -> list[int] | None:
    return None if item is None else enc(item)


def split_cases() -> list:
    cases = []
    for name, (pattern, flags) in SPLIT_PATTERNS.items():
        compiled = re.compile(pattern, flags)
        texts = make_texts(name, 260 if name in CHUNKING else 120)
        assert not any(joinable(t) for t in texts)
        for text in texts:
            for maxsplit in [0]:
                cases.append(split_case(name, compiled, text, maxsplit))
        for text in texts[:50 if name in CHUNKING else 25]:
            for maxsplit in [1, 2, 3, -1]:
                cases.append(split_case(name, compiled, text, maxsplit))
    return cases


def split_case(name: str, compiled: re.Pattern, text: str, maxsplit: int) -> dict:
    result = compiled.split(text, maxsplit)
    assert all(item is None or not joinable(item) for item in result)
    return {'p': name, 'n': maxsplit, 's': enc(text), 'r': [split_value(item) for item in result]}


# ---- $ of GAPS F7 ----

DOLLAR_TEXTS = [
    '', 'abc', 'abc\n', 'abc\n\n', '\n', '\n\n', 'a\nb', 'a\nb\n', 'a\n\nb\n', 'abc\r\n', 'abc\r', 'abc ',
    'abc\u0085', 'a b', 'abc\x0b', 'abc\x0c', '1abc', '_', '_\n', '-', 'a-b\n', 'a-b', 'é', 'a\U0001F600',
    'a\U0001F600\n', 'a\ud800', 'a\ud800\n', 'Valid_Label\n', 'Bad Label\n', '\nabc',
]


def dollar_cases() -> list:
    label = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
    dollar = re.compile(r'$')
    dollar_m = re.compile(r'$', re.MULTILINE)
    cases = []
    for text in DOLLAR_TEXTS:
        cases.append({
            's': enc(text),
            # helpers.py:35, SAFE_CYPHER_IDENTIFIER_PATTERN.search(...)
            'label': label.search(text) is not None,
            # helpers.py:165, re.match(r'^[a-zA-Z0-9_-]+$', group_id)
            'group_id': re.match(r'^[a-zA-Z0-9_-]+$', text) is not None,
            'dollar': [m.start() for m in dollar.finditer(text)],
            'dollar_multiline': [m.start() for m in dollar_m.finditer(text)],
        })
    return cases


# ---- str.replace with a count and str.count(sub) ----

TEXT_POOL = [
    '', 'a', 'b', 'abc', 'aaa', 'aaaa', 'abab', 'ababab', 'aXbXc', ' x ', '  ', 'héllo wörld', 'Straße',
    '\U0001F600', '\U0001F600\U0001F600', 'a\U0001F600b\U0001F600c', 'éé', 'é', '\ud800',
    'a\ud800b', '\udc00x\ud800', '\ud800\ud800', '\udc00\udc00', '\ud800a\udc00', 'a\U0001F600\ud800',
    '\U0001F600́', ' a ', ' x', '\n\nabc\n', 'xſy',
]
OLD_POOL = ['', 'a', 'aa', 'ab', 'b', 'abc', 'zzz', 'x', ' ', 'X', '\U0001F600', '\ud800', '\udc00', 'é', '́', 'ba']
NEW_POOL = ['', '-', 'XY', '\U0001F600', '\ud800', 'é', 'a', '́']
COUNT_POOL = [-2, -1, 0, 1, 2, 3, 100, 2**62]
SUB_POOL = OLD_POOL + ['\U0001F600\U0001F600', 'aaa', 'abab']


def replace_cases() -> list:
    cases = []
    index = 0
    for s in TEXT_POOL:
        for old in OLD_POOL:
            for count in COUNT_POOL:
                new = NEW_POOL[index % len(NEW_POOL)]
                index += 1
                result = s.replace(old, new, count)
                if joinable(result):
                    continue
                cases.append({'s': enc(s), 'old': enc(old), 'new': enc(new), 'n': count, 'r': enc(result)})
    rng = random.Random('regex-replace')
    tokens = ['a', 'b', 'ab', ' ', '\U0001F600', '́', '\ud800', '\udc00', 'x']
    for _ in range(600):
        s = ''.join(rng.choice(tokens) for _ in range(rng.randint(0, 10)))
        old = ''.join(rng.choice(tokens) for _ in range(rng.randint(0, 3)))
        new = ''.join(rng.choice(tokens + ['']) for _ in range(rng.randint(0, 3)))
        count = rng.choice([-1, 0, 1, 2, 3, 5])
        if joinable(s) or joinable(old) or joinable(new):
            continue
        result = s.replace(old, new, count)
        if joinable(result):
            continue
        cases.append({'s': enc(s), 'old': enc(old), 'new': enc(new), 'n': count, 'r': enc(result)})
    return cases


def count_cases() -> list:
    cases = []
    rng = random.Random('regex-count')
    for s in TEXT_POOL:
        for sub in SUB_POOL:
            cases.append({'s': enc(s), 'sub': enc(sub), 'r': s.count(sub)})
    tokens = ['a', 'b', 'ab', ' ', '\U0001F600', '́', '\ud800', '\udc00', 'x']
    for _ in range(400):
        s = ''.join(rng.choice(tokens) for _ in range(rng.randint(0, 12)))
        sub = ''.join(rng.choice(tokens) for _ in range(rng.randint(0, 3)))
        if joinable(s) or joinable(sub):
            continue
        cases.append({'s': enc(s), 'sub': enc(sub), 'r': s.count(sub)})
    return cases


# ---- the re.IGNORECASE patterns of temporal_edge_utils.py:14, :27, :32 ----

TEMPORAL_PATTERNS = {
    'future': re.compile(
        r'\b('
        r'will|shall|is going to|are going to|going to|'
        r'plans? to|planned to|planning to|'
        r'intends? to|intended to|'
        r'expects? to|expected to|'
        r'promises? to|promised to|'
        r'scheduled to|is scheduled to|are scheduled to|'
        r'set to|aims? to|aimed to|committed to'
        r")\b|[a-z]+'ll\b",
        re.IGNORECASE,
    ),
    'deadline': re.compile(r'\b(by|before|no later than|deadline|due by|due on)\b', re.IGNORECASE),
    'ongoing': re.compile(
        r'\b(until|through|thru|expires?|expired|ends?|ended|ending|'
        r'stops?|stopped|ceases?|ceased|no longer)\b',
        re.IGNORECASE,
    ),
}
TEMPORAL_WORDS = {
    'future': ['will', 'shall', 'is going to', 'going to', 'plans to', 'plan to', 'planned to', 'intends to', 'expected to',
               'promised to', 'is scheduled to', 'scheduled to', 'set to', 'aims to', 'committed to', "he'll", "they'll",
               "I'll", 'it will', 'willing', 'goings to'],
    'deadline': ['by', 'before', 'no later than', 'deadline', 'due by', 'due on', 'bye', 'standby', 'by2', 'by_'],
    'ongoing': ['until', 'through', 'thru', 'expire', 'expires', 'expired', 'end', 'ends', 'ended', 'ending', 'stop', 'stops',
                'stopped', 'cease', 'ceases', 'ceased', 'no longer', 'trend', 'friend'],
}
# U+A7CB and U+A7DC are not assigned in Unicode 15, but .NET 10 (Unicode 16) folds them to U+0264 and U+019B,
# which are letters. U+017F, U+0130, and U+0131 are the three characters of R13's IGNORECASE row.
TEMPORAL_SPECIAL = ['Ɤ', 'Ƛ', 'ɤ', 'ƛ', 'ſ', 'İ', 'ı', 'K', '\U0001F600',
                    '\U0001D400', 'é', '_', '1']
TEMPORAL_SEPARATORS = [' ', ' ', '.', '-', "'", '\n', ',', ' ', '\x1c']


def vary(rng: random.Random, word: str) -> str:
    out = []
    for ch in word:
        if ch == 's' and rng.random() < 0.3:
            ch = rng.choice(['ſ', 'S'])
        elif ch == 'i' and rng.random() < 0.3:
            ch = rng.choice(['İ', 'ı', 'I'])
        elif rng.random() < 0.1:
            ch = ch.upper()
        out.append(ch)
    return ''.join(out)


def temporal_cases() -> list:
    cases = []
    for name, compiled in TEMPORAL_PATTERNS.items():
        rng = random.Random(f'regex-temporal-{name}')
        words = TEMPORAL_WORDS[name]
        texts: dict[str, None] = {'': None, 'will': None, 'by': None, 'until': None}
        for word in words:
            for special in TEMPORAL_SPECIAL:
                texts[word + special] = None
                texts[special + word] = None
                texts[word.upper() + special] = None
            texts[word] = None
            texts[word.upper()] = None
            texts[f'x {word} y'] = None
        while len(texts) < 700:
            parts = []
            for _ in range(rng.randint(1, 5)):
                roll = rng.random()
                if roll < 0.5:
                    parts.append(vary(rng, rng.choice(words)))
                elif roll < 0.7:
                    parts.append(rng.choice(TEMPORAL_SPECIAL))
                else:
                    parts.append(rng.choice(TEMPORAL_SEPARATORS))
            texts[''.join(parts)] = None
        for text in texts:
            m = compiled.search(text)
            cases.append({'p': name, 's': enc(text), 'r': None if m is None else [m.start(), m.end()]})
    return cases


def build() -> tuple[str, str]:
    flags = class_flags()
    values = {
        'python': sys.version.split()[0],
        'unicode': unicodedata.unidata_version,
        'classes': {name: to_ranges(f) for name, f in flags.items()},
        'boundary': {'probes': BOUNDARY_PROBES, 'runs': boundary_runs()},
        'cases': {
            'split': split_cases(),
            'dollar': dollar_cases(),
            'temporal': temporal_cases(),
            'replace': replace_cases(),
            'count': count_cases(),
        },
    }
    values_text = json.dumps(values, separators=(',', ':'), ensure_ascii=False) + '\n'
    return values_text, data_source(flags)


def main() -> int:
    check = '--check' in sys.argv[1:]
    values_text, data_text = build()
    stale = []
    for path, text in ((VALUES_PATH, values_text), (DATA_PATH, data_text)):
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
        print('generate_regex_values: up to date')
    return 0


if __name__ == '__main__':
    sys.exit(main())
