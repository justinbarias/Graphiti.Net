"""Records results of graphiti_core/utils/content_chunking.py from real Python, for the evaluation tests.

The judge has 179 cases for this module, and the upstream tests are property tests. Neither finds a
wrong chunk boundary, a wrong overlap, or a wrong whitespace set. This file gives the evaluation tests
about 1,500 calls with generated inputs from a fixed seed (port/RULES.md, R23 and R25), so that the port
can be compared with them chunk for chunk.

    .upstream/graphiti/.venv/bin/python port/tools/record_chunking_values.py
    .upstream/graphiti/.venv/bin/python port/tools/record_chunking_values.py --check

It writes tests/Port.Evaluation/values/content_chunking.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.

Each case holds the function name, the arguments, and the result: the value, or the exception class and
its message. The recording leaves out the inputs that differ by a documented limit: a number above 64
bits, generate_covering_chunks with a chunk size of 0 or 1 and 4 items or more (decision D43), and the
random sampling path (GAPS F9). A high surrogate next to a low one is also left out: Python
counts them as two code points and C# reads them as one pair (RULES.md, Part 3).
"""
from __future__ import annotations

import json
import logging
import random
import sys
from math import comb
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'content_chunking.json'

WORDS = ['alpha', 'beta', 'Gamma', 'DELTA', 'Paris', 'London', 'iPhone', 'x', 'a', 'I', 'Zep', 'the', 'of',
         'Mc', 'OK', 'éclair', 'École', 'İstanbul', 'ǅungla', 'ΣΑΣ',
         'straße', 'Δelta', 'São', 'Hello,', '"Quoted"', '(Paren)', '[Bracket]', 'end.', 'Why?',
         'Wow!', 'semi;', 'colon:', 'A1', '1A', 'R2-D2', 'a.b.c', '...', '!?', '¿Qué?']
CJK = ['東京大学', 'こんにちは。', '中文！', '한국어']
EMOJI = ['\U0001F600', '\U0001F44D\U0001F3FD', '\U0001F1EF\U0001F1F5', '\U0001D400bc', '\U00020000']
SEPS = [' ', ' ', ' ', ' ', '. ', '! ', '? ', '.\n', '\n', '\r\n', '\n\n', '\n \n', '\n\t\n', '\n\n\n', '\t',
        ' ', '  ', '　', '\x1c', '\x1f', '\x85', '\xa0', '\r', '\x0b', '\x0c', '.  ', '. ']
SPEAKERS = ['Alice:', 'Bob:', 'Bob Smith:', '_bot:', 'user1:', 'A:', 'Speaker 2:', 'x_y z:', 'assistant:',
            'Émile:', '9lives:', 'Alice :', 'Alice\t:']

rng = random.Random(20261126)
records: list[dict] = []


def rand_text(max_tokens: int = 60, speaker: bool = False) -> str:
    parts: list[str] = []
    for i in range(rng.randint(0, max_tokens)):
        r = rng.random()
        if speaker and (i == 0 or parts and parts[-1] in ('\n', '\r\n', '\n\n')) and rng.random() < 0.7:
            parts.append(rng.choice(SPEAKERS))
            parts.append(rng.choice([' ', '', '  ']))
            continue
        if r < 0.55:
            parts.append(rng.choice(WORDS))
        elif r < 0.62:
            parts.append(rng.choice(CJK))
        elif r < 0.67:
            parts.append(rng.choice(EMOJI))
        elif r < 0.69:
            # A high surrogate is never next to a low one.
            parts.append(rng.choice(['\ud800z', '\udc00x', 'a\udfff']))
        elif r < 0.72 and not speaker:
            parts.append(rng.choice(SPEAKERS))
        else:
            parts.append(rng.choice(SEPS))
        if rng.random() < 0.6:
            parts.append(rng.choice(SEPS) if rng.random() < 0.4 else ' ')
    return ''.join(parts)


def rand_scalar():
    r = rng.random()
    if r < 0.25:
        return rng.randint(-1000, 1000)
    if r < 0.45:
        return rng.choice([0.1, 1.5, -0.0, 1e16, 1e-7, 123456789.123, 1e22, 5e-324, 1.7976931348623157e308,
                           0.30000000000000004, 2.5e-5, float('inf'), float('-inf')])
    if r < 0.5:
        return rng.choice([True, False, None])
    return rand_text(5)


def rand_key() -> str:
    return rng.choice(['id', 'name', 'k', '名前', 'café', 'emoji\U0001F600', 'a"b', 'tab\t',
                       '', 'key_' + str(rng.randint(0, 50)), 'K', 'nested', 'x\\y', 'É'])


def rand_value(depth: int = 0):
    r = rng.random()
    if depth < 3 and r < 0.2:
        return [rand_value(depth + 1) for _ in range(rng.randint(0, 3))]
    if depth < 3 and r < 0.4:
        return {rand_key(): rand_value(depth + 1) for _ in range(rng.randint(0, 3))}
    return rand_scalar()


def rand_list() -> list:
    return [rand_value(1) for _ in range(rng.randint(0, 16))]


def rand_dict() -> dict:
    return {rand_key() + ('' if rng.random() < 0.5 else str(i)): rand_value(1) for i in range(rng.randint(0, 16))}


def json_text(value) -> str:
    r = rng.random()
    if r < 0.5:
        return json.dumps(value)
    if r < 0.65:
        return json.dumps(value, ensure_ascii=False)
    if r < 0.75:
        return json.dumps(value, indent=rng.choice([0, 2]))
    if r < 0.85:
        return json.dumps(value, separators=(',', ':'))
    return ' \n' + json.dumps(value) + '\t\r\n'


SPECIAL_JSON = ['{"a": 1, "b": 2, "a": 3}', '[NaN, Infinity, -Infinity]', '[1E400, -0, -0.0, 1.0e+2, 1e5, 1.5e-7]',
                ' [1, 2]', '﻿[1]', '[1, 2', '{"a": }', '[1] x', '"str"', '123', 'true', 'null', '',
                '[01]', '[1.]', '["\\ud800"]', '["\\ud83d\\ude00", "\\u00e9"]', '{"k": [1, {"x": {"y": 1}}]}',
                '[' * 70 + ']' * 70, '[{"a":1},{"b":2}]' * 2, '[1, 2, 3,]', "{'a': 1}", '[\n]', '{}', '[]',
                '{"\\u0000": "\\u001f"}', '[true, false, null]']


def rand_json_content() -> str:
    r = rng.random()
    if r < 0.15:
        return rng.choice(SPECIAL_JSON)
    if r < 0.5:
        return json_text(rand_list())
    if r < 0.85:
        return json_text(rand_dict())
    if r < 0.92:
        return json_text(rand_scalar())
    t = json_text(rand_list())
    return t[: rng.randint(0, len(t))]


def rand_size(lo: int = -5, hi: int = 300) -> int:
    if rng.random() < 0.1:
        return rng.choice([0, 1, 2, 3, -1])
    return rng.randint(lo, hi)


def rand_tokens_opt():
    r = rng.random()
    if r < 0.15:
        return None
    if r < 0.25:
        return 0
    if r < 0.3:
        return rng.choice([-3, -1, 1])
    return rng.randint(1, 60)


def call(name: str, fn, args: list, encoded: list | None = None) -> None:
    try:
        result = {'ok': fn(*args)}
    except Exception as error:  # noqa: BLE001
        result = {'err': [type(error).__name__, str(error)]}
    records.append({'f': name, 'a': args if encoded is None else encoded, 'r': result})


def record(cc, episode_type) -> None:
    kinds = ['message', 'json', 'text', 'fact_triple']

    for _ in range(60):
        call('estimate_tokens', cc.estimate_tokens, [rand_text(rng.choice([5, 30, 100]))])

    # A number above 64 bits is left out: the port throws OverflowException (R4).
    for value in [0, 1, -1, 7, -250, 3000, 2 ** 31, 2 ** 40, -(2 ** 40)]:
        call('_tokens_to_chars', cc._tokens_to_chars, [value])
    for _ in range(20):
        call('_tokens_to_chars', cc._tokens_to_chars, [rng.randint(-10 ** 6, 10 ** 6)])

    for _ in range(60):
        d = rand_dict()
        call('_count_json_keys', cc._count_json_keys, [d, rng.randint(-1, 4), rng.randint(-1, 3)])

    for _ in range(60):
        c = rand_json_content()
        call('_json_likely_dense', cc._json_likely_dense,
             [c, rng.choice([0, -2, 1, 3, rng.randint(1, 400), cc.estimate_tokens(c)])])

    for _ in range(80):
        c = rand_text(rng.choice([10, 40, 100]))
        call('_text_likely_dense', cc._text_likely_dense,
             [c, rng.choice([0, -1, 1, 2, rng.randint(1, 100), cc.estimate_tokens(c)])])

    for _ in range(40):
        c = rand_json_content() if rng.random() < 0.5 else rand_text(60)
        call('_estimate_high_density', lambda c, e, t: cc._estimate_high_density(c, episode_type(e), t),
             [c, rng.choice(kinds), rng.choice([0, 1, rng.randint(1, 200), cc.estimate_tokens(c)])])

    for _ in range(40):
        r = rng.random()
        if r < 0.4:
            c = json.dumps([rng.randint(0, 9) for _ in range(rng.randint(500, 1500))])
        elif r < 0.7:
            c = ' '.join(rng.choice(['Alice', 'Bob', 'went', 'to', 'Paris.', 'the', 'London', 'IBM'])
                         for _ in range(rng.randint(600, 1200)))
        else:
            c = rand_text(rng.choice([30, 300]))
        call('should_chunk', lambda c, e: cc.should_chunk(c, episode_type(e)), [c, rng.choice(kinds)])

    for _ in range(100):
        call('chunk_json_content', cc.chunk_json_content, [rand_json_content(), rand_tokens_opt(), rand_tokens_opt()])

    for _ in range(110):
        call('chunk_text_content', cc.chunk_text_content,
             [rand_text(rng.choice([20, 60, 200])), rand_tokens_opt(), rand_tokens_opt()])

    for _ in range(100):
        r = rng.random()
        c = rand_json_content() if r < 0.25 else rand_text(rng.choice([20, 60, 200]), speaker=r < 0.8)
        call('chunk_message_content', cc.chunk_message_content, [c, rand_tokens_opt(), rand_tokens_opt()])

    for name, fn, count in [('_chunk_json_array', cc._chunk_json_array, 60), ('_chunk_message_array', cc._chunk_message_array, 30)]:
        for _ in range(count):
            call(name, fn, [rand_list(), rand_size(-5, 300), rand_size(-5, 300)])

    for _ in range(60):
        call('_chunk_json_object', cc._chunk_json_object, [rand_dict(), rand_size(-5, 300), rand_size(-5, 300)])

    for _ in range(40):
        call('_get_overlap_elements', cc._get_overlap_elements, [rand_list(), rand_size(-5, 200)])

    for _ in range(40):
        d = rand_dict()
        keys = list(d.keys())
        r = rng.random()
        if r < 0.3:
            keys = keys + ['missing', rand_key()]
            rng.shuffle(keys)
        elif r < 0.5 and keys:
            keys = keys + keys[: rng.randint(0, len(keys))]
        elif r < 0.6:
            keys = []
        call('_get_overlap_dict', cc._get_overlap_dict, [d, keys, rand_size(-5, 200)])

    for _ in range(60):
        call('_get_overlap_text', cc._get_overlap_text, [rand_text(rng.choice([5, 30])), rand_size(-10, 100)])

    for _ in range(40):
        call('_get_overlap_messages', cc._get_overlap_messages,
             [[rand_text(6) for _ in range(rng.randint(0, 8))], rand_size(-5, 150)])

    for _ in range(80):
        call('_chunk_by_size', cc._chunk_by_size, [rand_text(rng.choice([5, 30, 100])), rand_size(-8, 150), rand_size(-8, 180)])

    for name, fn, speaker in [('_chunk_by_sentences', cc._chunk_by_sentences, False),
                              ('_chunk_by_lines', cc._chunk_by_lines, False),
                              ('_chunk_speaker_messages', cc._chunk_speaker_messages, True)]:
        for _ in range(70):
            t = rand_text(rng.choice([10, 40, 120]), speaker=speaker and rng.random() < 0.8)
            call(name, fn, [t, rand_size(-5, 200), rand_size(-5, 250)])

    # Inputs that the judge cases miss, each one fixed by a seeded defect of the review.
    call('_chunk_speaker_messages', cc._chunk_speaker_messages, ['A: x\nA: xxxxxx\nC: xxx\nA: \nB: xxxx\nB: ', 13, 9])
    call('_chunk_by_lines', cc._chunk_by_lines, ['dd ee\na\nb c\nb c\na', 7, 4])
    # The whitespace set of \s includes U+001C to U+001F (GAPS F17).
    call('chunk_text_content', cc.chunk_text_content, ['aaaa\n\x1c\nbbbb', 1, 1])
    for ws in ['\x1c', '\x1d', '\x1e', '\x1f', '\x85', ' ', '　', '\xa0', '\x0b', '᠎', '​']:
        call('chunk_text_content', cc.chunk_text_content, ['aaaa\n' + ws + '\nbbbb', 1, 1])
        call('_chunk_by_sentences', cc._chunk_by_sentences, ['Aa.' + ws + 'Bb. Cc!' + ws + ws + 'Dd', 4, 0])
        call('chunk_message_content', cc.chunk_message_content, ['Al' + ws + 'ice: hi there\nBob: hello you', 2, 1])
        call('_chunk_speaker_messages', cc._chunk_speaker_messages, ['Al' + ws + 'ice: hi\n' + ws + 'Bob: yo', 5, 1])
        call('_text_likely_dense', cc._text_likely_dense, ['a' + ws + 'Bob' + ws + 'Carl', 1])
        call('_chunk_by_size', cc._chunk_by_size, [ws + 'abc def' + ws, 3, 1])

    # generate_covering_chunks: the early return and the enumeration path.
    for n in range(0, 14):
        for k in range(-2, n + 3):
            if n > k >= 0 and comb(n, k) > cc.MAX_COMBINATIONS_TO_EVALUATE:
                continue  # random sampling (GAPS F9)
            if k in (0, 1) and n >= 4:
                continue  # the order of the loop over a set (decision D43)
            items = [rng.choice(['A', 'B', 1, 2.5, None, True, 'é', [1], {'a': 1}]) for _ in range(n)]
            call('generate_covering_chunks', cc.generate_covering_chunks, [items, k])


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    from graphiti_core.nodes import EpisodeType
    from graphiti_core.utils import content_chunking

    record(content_chunking, EpisodeType)
    # allow_nan keeps inf and nan in the file as the tokens that PyJson.Loads reads.
    lines = [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","source":"graphiti_core/utils/content_chunking.py","cases":[\n'
        + ',\n'.join(lines) + '\n]}\n'
    )
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
