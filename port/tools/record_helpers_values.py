"""Records results of graphiti_core/helpers.py, utils/datetime_utils.py, and utils/text_utils.py from real Python.

The judge has about 200 cases for these modules, and the upstream tests are a few examples. Neither finds a wrong
escape table, a wrong sentence cut for text above U+FFFF, a wrong time zone conversion, or a last-bit difference of
the L2 norm. This file gives the evaluation tests about 2,000 calls with generated inputs from a fixed seed
(port/RULES.md, R23 and R25), so that the port can be compared with them call for call.

    .upstream/graphiti/.venv/bin/python port/tools/record_helpers_values.py
    .upstream/graphiti/.venv/bin/python port/tools/record_helpers_values.py --check

It writes tests/Port.Evaluation/values/helpers_values.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream in .upstream/graphiti. It changes
nothing there. Nothing here uses the network.

Each case holds the function name, the arguments, and the result: the value, or the exception class and its
message. A datetime is {"$dt": isoformat}, a tuple is {"$tuple": [...]}, and a dict with a key that is not text is
{"$dict": [[key, value], ...]}. An episode is {"uuid", "content", "valid_at"}. A module-level constant is the case {"f": "constant", "a": ["helpers.NAME"]}.
A semaphore_gather case holds the count of calls and max_coroutines, and its result is the list of results and the
largest count of calls that ran at the same time.
"""
from __future__ import annotations

import asyncio
import json
import os
import random
import re
import sys
import warnings
from datetime import date, datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'helpers_values.json'

# The constants of helpers.py read these variables at import. The recording uses the defaults.
ENV_NAMES = ['SEMAPHORE_LIMIT', 'USE_PARALLEL_RUNTIME', 'CHUNK_TOKEN_SIZE', 'CHUNK_OVERLAP_TOKENS', 'CHUNK_MIN_TOKENS',
             'CHUNK_DENSITY_THRESHOLD']
rng = random.Random(20261127)
records: list[dict] = []

WORDS = ['alpha', 'ORNTAD', 'orntad', 'AND', 'OR', 'NOT', 'The', 'Zep', 'x', 'a', 'Paris', 'TODAY', 'Rand', 'DANTE',
         'café', 'École', 'straße', 'İstanbul', '東京', 'こんにちは', '한국어', 'Ａｂｃ', '１２３', '٣٤٥']
SPECIAL = list('+-&|!(){}[]^"~*?:\\/') + ['&&', '||', '\\\\', '\\"', '\\n']
EMOJI = ['\U0001F600', '\U0001F44D\U0001F3FD', '\U0001F1EF\U0001F1F5', '\U0001D400', '\U00020000', '\U0010FFFF']
MARKS = ['é', '́', '​', ' ', ' ', ' ', '\x85', '\x0b', '\x0c', '\x1c', '\x1f', '　',
         '\t', '\r', '\n', '\r\n']
SENT = ['. ', '! ', '? ', '.\n', '!\t', '?\r', '.\x85', '. ', '. ', '.\x1c', '. ', '.  ', '.', '!', '?', '...',
        '。', '！', '? ', '.　']
LONE = ['\ud800', '\udc00', '\udfff', 'a\ud800b']


def rand_text(max_parts: int = 30, sentence: bool = False, lone: bool = False) -> str:
    parts: list[str] = []
    for _ in range(rng.randint(0, max_parts)):
        r = rng.random()
        if sentence and r < 0.3:
            parts.append(rng.choice(SENT))
        elif r < 0.45:
            parts.append(rng.choice(WORDS))
        elif r < 0.6:
            parts.append(rng.choice(SPECIAL))
        elif r < 0.68:
            parts.append(rng.choice(EMOJI))
        elif r < 0.78:
            parts.append(rng.choice(MARKS))
        elif lone and r < 0.8:
            parts.append(rng.choice(LONE))
        else:
            parts.append(rng.choice([' ', ' ', '', 'ab', 'Q', str(rng.randint(0, 99))]))
    return ''.join(parts)


def enc(value):
    if isinstance(value, datetime):
        return {'$dt': value.isoformat()}
    if isinstance(value, tuple):
        return {'$tuple': [enc(v) for v in value]}
    if isinstance(value, dict):
        if all(isinstance(k, str) for k in value):
            return {k: enc(v) for k, v in value.items()}
        return {'$dict': [[enc(k), enc(v)] for k, v in value.items()]}
    if isinstance(value, list):
        return [enc(v) for v in value]
    return value


def call(name: str, fn, args: list, encoded: list | None = None, out=lambda x: x) -> None:
    try:
        result = {'ok': enc(out(fn(*args)))}
    except Exception as error:  # noqa: BLE001
        result = {'err': [type(error).__name__, str(error)]}
    records.append({'f': name, 'a': enc(args) if encoded is None else encoded, 'r': result})


def rand_offset() -> timezone:
    r = rng.random()
    if r < 0.3:
        return timezone.utc
    if r < 0.4:
        return timezone(timedelta(0))
    if r < 0.8:
        return timezone(timedelta(minutes=rng.choice([60, -60, 330, -330, 345, 540, -480, -720, 840, 1, -1, 90])))
    if r < 0.9:
        return timezone(timedelta(hours=rng.randint(-23, 23), minutes=rng.randint(0, 59)))
    return timezone(timedelta(seconds=rng.choice([15, -15, 3601, -86399, 86399])))


def rand_dt(allow_extreme: bool = True) -> datetime:
    r = rng.random()
    if allow_extreme and r < 0.06:
        base = rng.choice([datetime(1, 1, 1), datetime(1, 1, 1, 0, 0, 1), datetime(9999, 12, 31, 23, 59, 59, 999999),
                           datetime(9999, 12, 31), datetime(1, 1, 2), datetime(9999, 12, 30, 12)])
    else:
        base = datetime(rng.randint(1900, 2100), rng.randint(1, 12), rng.randint(1, 28), rng.randint(0, 23),
                        rng.randint(0, 59), rng.randint(0, 59), rng.choice([0, 0, 0, 1, 999999, rng.randint(0, 999999)]))
    tz = None if rng.random() < 0.3 else rand_offset()
    return base.replace(tzinfo=tz)


def rand_iso_string() -> str:
    d = date(rng.randint(1, 9999) if rng.random() < 0.1 else rng.randint(1970, 2100), rng.randint(1, 12), rng.randint(1, 28))
    r = rng.random()
    if r < 0.6:
        ds = d.isoformat()
    elif r < 0.75:
        ds = d.strftime('%Y%m%d')
    elif r < 0.9:
        iso = d.isocalendar()
        ds = f'{iso[0]:04d}-W{iso[1]:02d}-{iso[2]}' if rng.random() < 0.7 else f'{iso[0]:04d}W{iso[1]:02d}{iso[2]}'
    else:
        iso = d.isocalendar()
        ds = f'{iso[0]:04d}-W{iso[1]:02d}'
    if rng.random() < 0.15:
        return ds
    sep = rng.choice(['T', 'T', 'T', ' ', 't', 'x', '_'])
    h, m, s = rng.randint(0, 23), rng.randint(0, 59), rng.randint(0, 59)
    kind = rng.random()
    frac = ''
    if rng.random() < 0.4:
        frac = rng.choice(['.', ',', '.']) + ''.join(str(rng.randint(0, 9)) for _ in range(rng.choice([1, 2, 3, 6, 6, 7, 9])))
    if kind < 0.55:
        ts = f'{h:02d}:{m:02d}:{s:02d}{frac}'
    elif kind < 0.7:
        ts = f'{h:02d}:{m:02d}'
    elif kind < 0.8:
        ts = f'{h:02d}'
    elif kind < 0.92:
        ts = f'{h:02d}{m:02d}{s:02d}{frac}'
    else:
        ts = f'{h:02d}{m:02d}'
    tzk = rng.random()
    sign = rng.choice(['+', '-'])
    oh, om = rng.randint(0, 23), rng.randint(0, 59)
    if tzk < 0.3:
        tzs = ''
    elif tzk < 0.5:
        tzs = 'Z'
    elif tzk < 0.7:
        tzs = f'{sign}{oh:02d}:{om:02d}'
    elif tzk < 0.8:
        tzs = f'{sign}{oh:02d}{om:02d}'
    elif tzk < 0.88:
        tzs = f'{sign}{oh:02d}'
    elif tzk < 0.94:
        tzs = f'{sign}{oh:02d}:{om:02d}:{rng.randint(0, 59):02d}'
    else:
        tzs = f'{sign}{oh:02d}:{om:02d}:{rng.randint(0, 59):02d}.{rng.randint(0, 999999):06d}'
    text = ds + sep + ts + tzs
    r = rng.random()
    if r < 0.18:
        i = rng.randrange(len(text))
        text = text[:i] + rng.choice(['', 'x', ':', '-', '9', ' ', '١']) + text[i + 1:]
    elif r < 0.22:
        text = text + rng.choice([' ', 'Z', '0', '\n'])
    elif r < 0.25:
        text = rng.choice(['', 'not a date', '2025', '2025-13-01', '2025-02-30', '2025-01-15T24:00:00', '2025-01-15T10:60:00',
                           '0000-01-01', '2025-W54-1', '2025-W00-1', '٢٠٢٥-01-15', ' 2025-01-15',
                           '2025-01-15T10:30:00+24:00', '2025-01-15T10:30:00-23:59:59.999999'])
    return text


def rand_group_id() -> str:
    r = rng.random()
    if r < 0.5:
        return ''.join(rng.choice('abcXYZ019_-') for _ in range(rng.randint(0, 12)))
    return ''.join(rng.choice(['a', 'B', '_', '-', ' ', '.', 'é', '\n', '\t', '٣', '東', '\U0001F600', '1', '/', '\u0085',
                               '\ud800'])
                   for _ in range(rng.randint(0, 6)))


def rand_float() -> float:
    r = rng.random()
    if r < 0.5:
        return rng.uniform(-1, 1)
    if r < 0.7:
        return rng.gauss(0, 1) * 10 ** rng.randint(-8, 8)
    if r < 0.8:
        return float(rng.randint(-5, 5))
    if r < 0.85:
        return rng.choice([0.0, -0.0, 1e-200, -1e-200, 1e200, 5e-324, 1e-170, 1e154, 1e155, 3.0, 4.0])
    return rng.uniform(-1e3, 1e3)


def rand_value(depth: int = 0):
    r = rng.random()
    if depth < 3 and r < 0.15:
        return [rand_value(depth + 1) for _ in range(rng.randint(0, 3))]
    if depth < 3 and r < 0.3:
        return {rng.choice(['id', 'name', 'k', '名前', 'é', 'a"b', '', 'when', 'x' + str(i)]) + str(i): rand_value(depth + 1)
                for i in range(rng.randint(0, 3))}
    if depth < 3 and r < 0.4:
        return tuple(rand_value(depth + 1) for _ in range(rng.randint(0, 3)))
    if depth < 3 and r < 0.47:
        keys = [rng.choice([1, 2, 3, (1, 2), (rand_dt(False).isoformat(), 'x'), True, None, 2.5]) for _ in range(rng.randint(1, 3))]
        return {k: rand_value(depth + 1) for k in keys}
    if r < 0.7:
        return rand_dt()
    if r < 0.78:
        return rng.randint(-100, 100)
    if r < 0.84:
        return rng.choice([0.5, 1e22, -0.0, 1e-7])
    if r < 0.88:
        return rng.choice([True, False, None])
    return rand_text(4)


def record(helpers, datetime_utils, text_utils, GraphitiTypes) -> None:
    for _ in range(160):
        call('lucene_sanitize', helpers.lucene_sanitize, [rand_text(25, lone=rng.random() < 0.1)])
    call('lucene_sanitize', helpers.lucene_sanitize, ['ORNTAD' * 3 + '+-&|!(){}[]^"~*?:\\/'])

    for _ in range(120):
        call('validate_group_id', helpers.validate_group_id, [rand_group_id()])
    call('validate_group_id', helpers.validate_group_id, [None])
    call('validate_group_id', helpers.validate_group_id, ['abc\n'])
    for _ in range(50):
        ids = [rand_group_id() for _ in range(rng.randint(0, 5))]
        call('validate_group_ids', helpers.validate_group_ids, [ids])
    call('validate_group_ids', helpers.validate_group_ids, [None])

    for _ in range(40):
        labels = [rng.choice(['Person', 'Org_1', '_x', 'a-b', '1a', 'é', '', 'A B', 'x`y', 'abc\n', 'Z' * 5, '東', 'ok']) for _ in range(rng.randint(0, 4))]
        call('validate_node_labels', helpers.validate_node_labels, [labels])

    for _ in range(70):
        names = ['Person', 'Organization', 'Entity', 'Zeta', 'alpha', 'Alpha', 'é', 'Éclair', '東京', 'entity', '', '\U0001F600',
                 '\ue000', '\ufb01', '\uffff', '\U00010000', 'ﬁne']
        excluded = [rng.choice(names) for _ in range(rng.randint(0, 5))]
        r = rng.random()
        types = None if r < 0.3 else {n: object() for n in rng.sample(names, rng.randint(0, 4))}
        call('validate_excluded_entity_types', helpers.validate_excluded_entity_types, [excluded, types],
             encoded=[excluded, None if types is None else list(types)])
    call('validate_excluded_entity_types', helpers.validate_excluded_entity_types, [None, {}], encoded=[None, []])
    # Code point order and UTF-16 order differ between U+E000 to U+FFFF and the characters above U+FFFF.
    for excluded in (['\U0001F600', '\uffff', '\ue000'], ['\U00010000', '\ufb01', 'x'], ['\uffff', '\U0001F600'],
                     ['\U0001F600', '\ufb01', 'Person', '\uffff']):
        call('validate_excluded_entity_types', helpers.validate_excluded_entity_types, [excluded, None],
             encoded=[excluded, None])
        call('validate_excluded_entity_types', helpers.validate_excluded_entity_types, [excluded, {'\uffff': object()}],
             encoded=[excluded, ['\uffff']])

    # Vectors of 0 to 11 items, 100 to 300 items (the pairwise sum splits above 128), and 1,024 items.
    sizes = [0, 1, 2, 3, 4, 7, 8, 9, 15, 16, 17, 127, 128, 129, 130, 136, 255, 256, 257, 300, 1024, 1024, 1536]
    for n in sizes:
        for _ in range(2 if n > 500 else 4):
            call('normalize_l2', helpers.normalize_l2, [[rng.uniform(-1, 1) for _ in range(n)]], out=lambda a: a.tolist())
    for _ in range(80):
        n = rng.choice([1, 2, 3, 5, 8, 10, 33])
        call('normalize_l2', helpers.normalize_l2, [[rand_float() for _ in range(n)]], out=lambda a: a.tolist())
    for v in [[0.0, 0.0, 0.0], [-0.0, 0.0], [1e-200, 1e-200], [1e200, 1e200], [1e-320, 1e-320], [float('nan'), 1.0],
              [float('inf'), 1.0], [1.0, float('-inf')], [3.0, 4.0], [1.0, 0.0, 0.0, 0.0], [-5.0], [1e-10, 1.0]]:
        call('normalize_l2', helpers.normalize_l2, [v], out=lambda a: a.tolist())

    for _ in range(260):
        call('parse_db_date', helpers.parse_db_date, [rand_iso_string()], out=lambda d: d)
    for _ in range(25):
        call('parse_db_date', helpers.parse_db_date, [rand_dt()])
    call('parse_db_date', helpers.parse_db_date, [None])
    for value in (42, 0, True, False, 2.5, [1, 'a'], {'k': 1}, [], {}):
        call('parse_db_date', helpers.parse_db_date, [value])

    for _ in range(170):
        call('ensure_utc', datetime_utils.ensure_utc, [rand_dt()])
    call('ensure_utc', datetime_utils.ensure_utc, [None])

    for _ in range(140):
        call('convert_datetimes_to_strings', datetime_utils.convert_datetimes_to_strings, [rand_value()])

    for _ in range(150):
        text = rand_text(14, lone=rng.random() < 0.1)
        try:
            size = len(text.encode('utf-8'))
        except UnicodeEncodeError:
            size = len(text) * 3
        call('truncate_utf8_to_bytes', text_utils.truncate_utf8_to_bytes,
             [text, rng.choice([-3, 0, 1, 2, 3, 4, rng.randint(0, size + 3), rng.randint(0, size + 3), size, size - 1, 10 ** 12])])

    # A lone surrogate cannot be encoded. A budget of 0 or less returns before the encode, and a budget of 1 raises.
    for text, budget in [('\ud800', 0), ('\ud800', -1), ('\ud800', 1), ('a\udc00', 1), ('a\udc00', 0), ('\udfff', 2),
                         ('a\ud800b', 0), ('a\ud800b', -7), ('a\ud800b', 3), ('\ud800', 10 ** 12)]:
        call('truncate_utf8_to_bytes', text_utils.truncate_utf8_to_bytes, [text, budget])

    # A cut that ends on a white space character of Python with no sentence end before it. str.rstrip removes U+001C to U+001F.
    for ch in ['\x1c', '\x1d', '\x1e', '\x1f', '\x0b', '\x0c', '\x85', '\xa0', '\u1680', '\u2028', '\u2029', '\u3000',
               '\u200b', '\ufeff']:
        for text, n in [('abcd' + ch + 'efgh', 5), ('abc' + ch + ch + 'def', 5), ('ab' + ch + 'c' + ch + ch + ch + 'dd', 7),
                        (ch + ch + 'abcdef', 2), ('abcd' + ch + 'x. yz' + ch, 5), ('é' + ch + ch + 'ab😀', 3)]:
            call('truncate_at_sentence', text_utils.truncate_at_sentence, [text, n])

    for _ in range(220):
        text = rand_text(40, sentence=True)
        n = len(text)
        call('truncate_at_sentence', text_utils.truncate_at_sentence,
             [text, rng.choice([-5, -1, 0, 1, 2, 3, rng.randint(0, n + 2), rng.randint(0, n + 2), rng.randint(0, n + 2), n, n - 1, n + 10])])

    # Python: SEMAPHORE_LIMIT is 20 here, and max_coroutines of None or 0 gives that limit.
    async def run_gather(count: int, limit):
        running = 0
        peak = 0

        async def work(i: int) -> int:
            nonlocal running, peak
            running += 1
            peak = max(peak, running)
            for _ in range(4):
                await asyncio.sleep(0)
            running -= 1
            return i

        results = await helpers.semaphore_gather(*(work(i) for i in range(count)), max_coroutines=limit)
        return {'results': results, 'peak': peak}

    for count, limit in [(25, None), (25, 0), (25, 1), (25, 3), (25, 40), (30, None), (21, 0), (20, None), (3, 3), (7, 2),
                         (1, None), (0, None), (5, 1), (45, 20)]:
        call('semaphore_gather', lambda c, lim: asyncio.run(run_gather(c, lim)), [count, limit])

    for module_name, module in [('helpers', helpers), ('datetime_utils', datetime_utils), ('text_utils', text_utils)]:
        for name, value in vars(module).items():
            if not name.isupper() or name.startswith('__') or name == 'TYPE_CHECKING':
                continue
            if isinstance(value, re.Pattern):
                value = value.pattern
            records.append({'f': 'constant', 'a': [f'{module_name}.{name}'], 'r': {'ok': value}})

    def episode(content: str, valid_at: datetime, uuid: str):
        return {'uuid': uuid, 'content': content, 'valid_at': valid_at}

    def make(ep):
        return GraphitiTypes.make_episode(ep['uuid'], ep['content'], ep['valid_at'])

    for _ in range(90):
        eps = [episode(rand_text(6), rand_dt(False), rng.choice(['a', 'b', 'c', 'd', 'é'])) for _ in range(rng.randint(0, 4))]
        call('concatenate_episodes', lambda e: text_utils.concatenate_episodes([make(x) for x in e]), [eps],
             encoded=[[{'uuid': x['uuid'], 'content': x['content'], 'valid_at': enc(x['valid_at'])} for x in eps]])

    for _ in range(110):
        pool = [episode(rand_text(6), rand_dt(False), rng.choice(['a', 'b', 'c', 'd', 'e', 'é'])) for _ in range(rng.randint(0, 5))]
        episodes = [x for x in pool if rng.random() < 0.6]
        timeline = [dict(x) if rng.random() < 0.7 else episode(rand_text(3), rand_dt(False), x['uuid']) for x in pool]
        rng.shuffle(timeline)
        if rng.random() < 0.2:
            timeline = timeline + [episode(rand_text(3), rand_dt(False), 'z')]

        def pack(items):
            return [{'uuid': x['uuid'], 'content': x['content'], 'valid_at': enc(x['valid_at'])} for x in items]

        call('concatenate_timeline',
             lambda e, t: text_utils.concatenate_timeline([make(x) for x in e], [make(x) for x in t]),
             [episodes, timeline], encoded=[pack(episodes), pack(timeline)])


class Types:
    @staticmethod
    def make_episode(uuid: str, content: str, valid_at: datetime):
        from graphiti_core.nodes import EpisodeType, EpisodicNode
        return EpisodicNode(uuid=uuid, name='n', group_id='g', source=EpisodeType.message, source_description='d',
                            content=content, valid_at=valid_at)


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        print(f'This tool needs Python 3.12, and this is {sys.version}', file=sys.stderr)
        return 2
    sys.path.insert(0, str(UPSTREAM))
    warnings.simplefilter('ignore')
    for name in ENV_NAMES:
        os.environ.pop(name, None)
    from graphiti_core import helpers
    from graphiti_core.utils import datetime_utils, text_utils

    # helpers.py calls load_dotenv(), so a .env file could set a variable again.
    for name in ENV_NAMES:
        if name in os.environ:
            print(f'{name} is set after the import (a .env file?). The recording needs the defaults.', file=sys.stderr)
            return 2

    record(helpers, datetime_utils, text_utils, Types)

    lines = [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in records]
    text = ('{"python":"%s","source":"graphiti_core/helpers.py, utils/datetime_utils.py, utils/text_utils.py","cases":[\n'
            % sys.version.split()[0]) + ',\n'.join(lines) + '\n]}\n'
    if '--check' in sys.argv[1:]:
        if OUT.exists() and OUT.read_text() == text:
            print(f'{OUT.name}: current ({len(records)} cases)')
            return 0
        print(f'{OUT.name}: differs from the recording', file=sys.stderr)
        return 1
    OUT.write_text(text)
    print(f'{OUT.name}: wrote {len(records)} cases, {len(text)} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
