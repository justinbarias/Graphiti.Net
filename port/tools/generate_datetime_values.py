"""Records what real Python gives for the PyDateTime helper of PyCompat (plan 0003, task P4).

One file comes out of one run:

- tests/Graphiti.Net.Property.Tests/python-values.datetime.json: inputs and the Python result of each one.
  The property tests in DateTimeTests.cs read it. A result is a value or the name and the message of a Python error.

    .upstream/graphiti/.venv/bin/python port/tools/generate_datetime_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_datetime_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.
A text is a plain JSON string. A text with a lone surrogate is a list of code points, because JSON text cannot carry one.
A datetime is a list [year, month, day, hour, minute, second, microsecond, offset], where the offset is the
utcoffset in microseconds and null for a naive value.
The random cases use a fixed seed. Nothing here uses the network.
"""
from __future__ import annotations

import json
import random
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.datetime.json'
SEED = 20261126

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_datetime_values: needs Python 3.12, found {sys.version.split()[0]}')

from neo4j.time import DateTime as Neo4jDateTime  # noqa: E402  (the package of the upstream environment)

US = timedelta(microseconds=1)
UTC = timezone.utc


def cps(s: str) -> list[int]:
    return [ord(c) for c in s]


def T(s: str):
    """A text for the values file: a plain string, or a list of code points if it holds a lone surrogate."""
    return cps(s) if any(0xD800 <= ord(c) <= 0xDFFF for c in s) else s


def has_joinable_surrogates(s: str) -> bool:
    return any(0xD800 <= ord(a) <= 0xDBFF and 0xDC00 <= ord(b) <= 0xDFFF for a, b in zip(s, s[1:]))


def off_us(dt: datetime) -> int | None:
    off = dt.utcoffset()
    return None if off is None else off // US


def D(dt: datetime) -> list:
    return [dt.year, dt.month, dt.day, dt.hour, dt.minute, dt.second, dt.microsecond, off_us(dt)]


def mk(y: int, mo: int, d: int, h: int, mi: int, s: int, us: int, off: int | None) -> datetime:
    tz = None if off is None else timezone(timedelta(microseconds=off))
    return datetime(y, mo, d, h, mi, s, us, tz)


def failure(e: Exception) -> dict:
    return {'e': type(e).__name__, 'msg': T(str(e))}


def result_or_error(fn) -> dict:
    """The result of fn as a case body: {'r': datetime} or the error that Python raised."""
    try:
        return {'r': D(fn())}
    except (ValueError, OverflowError, TypeError) as e:
        return failure(e)


# ---- the pool of datetimes ----

DATES = [(1, 1, 1), (9999, 12, 31), (2024, 2, 29), (2025, 1, 3), (1999, 12, 31), (1900, 3, 1), (2000, 2, 29),
         (1970, 1, 1), (2023, 7, 4), (2100, 12, 31)]
TIMES = [(0, 0, 0, 0), (23, 59, 59, 999999), (12, 0, 0, 1), (1, 2, 3, 4), (13, 45, 30, 500000), (0, 0, 0, 999999),
         (11, 59, 59, 0), (12, 30, 0, 100000)]
SECOND = 1_000_000
OFFSETS = [None, 0, 19800 * SECOND, -28800 * SECOND, 30 * SECOND, -1 * SECOND, 86399 * SECOND + 999999,
           -86340 * SECOND, 500000, 3600 * SECOND]

POOL = [mk(*d, *t, off) for d in DATES for t in TIMES for off in OFFSETS]


def small_pool(rng: random.Random, count: int) -> list[datetime]:
    extremes = [mk(1, 1, 1, 0, 0, 0, 0, None), mk(1, 1, 1, 0, 0, 0, 0, 0), mk(9999, 12, 31, 23, 59, 59, 999999, None),
                mk(9999, 12, 31, 23, 59, 59, 999999, 0), mk(1, 1, 1, 0, 0, 0, 0, 5 * 3600 * SECOND),
                mk(9999, 12, 31, 23, 59, 59, 999999, -8 * 3600 * SECOND)]
    return extremes + rng.sample(POOL, count)


# ---- iso_format, str, repr ----

def text_cases() -> dict:
    return {
        'iso_format': [{'dt': D(dt), 'r': T(dt.isoformat())} for dt in POOL],
        'str': [{'dt': D(dt), 'r': T(str(dt))} for dt in POOL],
        'repr': [{'dt': D(dt), 'r': T(repr(dt))} for dt in POOL],
    }


# ---- fromisoformat ----

DATE_FORMS = [
    '2025-01-03', '20250103', '2025-W01', '2025W01', '2025-W01-1', '2025W011', '2025-W52-7', '2025W527',
    '2026-W53-4', '2026W534', '2025-W53-1', '2025-W00-1', '2025-W54-1', '2025-W01-0', '2025-W01-8', '2025-W1-1',
    '2024-02-29', '2023-02-29', '2000-02-29', '1900-02-29', '2100-02-29', '2024-04-31', '2024-01-32', '2024-00-10',
    '2024-01-00', '2024-13-01', '9999-12-31', '0001-01-01', '0000-01-01', '9999-W52-5', '9999-W52-6', '9999-W52-7',
    '9999-W53-1', '0001-W01-1', '0000-W01-1', '1970-01-01', '2020-W53-7', '2015-W53-4', '1998-W53-5', '2025-1-03',
    '2025-01-3', '25-01-03', '2025/01/03', '2025.01.03', '2025-0103', '202501-03', '2025-01-03-', '2025-W01-',
    '2025W01-1', '2025-W011', '2025-W01-12', '2025-W01-1-', '-2025-01-03', '+2025-01-03',
]
TIME_FORMS = [
    '', '10', '10:30', '1030', '10:30:45', '103045', '10:30:45.5', '10:30:45.123', '10:30:45.123456',
    '10:30:45.1234567', '10:30:45.123456789', '10:30:45,123', '103045.5', '103045,123456', '10:30:45.000001',
    '10:30:45.999999', '10:30:45.9999999', '24:00', '24:00:00', '23:59:59', '23:60', '23:59:60', '00:00:00',
    '00:00', '25:00', '1', '1:30', '10:3', '10:30:4', '10:30:45.', '10:30:45,', '10:30:', '10:', ':30', '10.5',
    '10:30.5', '1030:45', '10:3045', '10:30:45:5', '10:30:45x', '10 30', '10:30 ', ' 10:30', '10:30:45.12a',
    '10:30:45.1 ', '1030455', '10304', '103', '٠٥:٣٠', '１０:３０', '10:30:45.１２３',
]
TZ_FORMS = [
    '', 'Z', 'z', '+00:00', '-00:00', '+0000', '+00', '-00', '+05:30', '-05:30', '+0530', '-0800', '+05', '-08',
    '+05:30:15', '+053015', '+05:30:15.5', '+05:30:15.123456', '+05:30:15.123456789', '-05:30:15.5', '+00:00:00.5',
    '-00:00:00.5', '+00:00:30', '+23:59', '+23:59:59.999999', '+24:00', '-24:00', '+24:01', '+99:99', '+00:99',
    '+5:30', '+05:3', '+05:30:1', '+05:', '+', '-', 'Z ', 'ZZ', 'Z+00:00', '+00:00Z', '+05:30 ', ' +05:30',
    '+0530:15', '+05:3015', '+05:30:15:5', '+05.5', '+05:30,5', '+٠٥:٣٠', '+０５:３０',
]
SEPARATORS = ['T', ' ', 't', '_', 'x', '/', '\t', '\n', '-', '.', ',', 'é', '€', '😀', '\x00', '', 'TT', ' T']


def iso_corpus(rng: random.Random) -> list[str]:
    out: list[str] = []
    # Python's own output for a sample of the pool, in both forms.
    for dt in small_pool(rng, 90):
        out.append(dt.isoformat())
        out.append(str(dt))
        out.append(dt.isoformat(timespec='milliseconds'))
        out.append(dt.isoformat(timespec='minutes'))
        out.append(dt.date().isoformat())
    # Each date form with each time form, and each time form with each zone.
    for date in DATE_FORMS:
        out.append(date)
        for time in ['10:30', '10:30:45.123456', '1030', '']:
            out.append(date + 'T' + time)
            out.append(date + ' ' + time)
    for time in TIME_FORMS:
        for tz in TZ_FORMS:
            out.append('2025-01-03T' + time + tz)
    for date in ['2025-01-03', '20250103', '2025-W01-1', '2025W011', '2025-W01', '2025W01']:
        for sep in SEPARATORS:
            out.append(date + sep + '10:30:45')
            out.append(date + sep + '10:30:45+05:30')
            out.append(date + sep)
    # Short and odd text.
    out += ['', ' ', 'T', '2025', '202501', '2025-0', '2025-01', '2025-01-0', '20250', '2025010', '2025-W0',
            '2025-W1', '2025W1', '2025W', '2025-W', 'abcdefg', 'abcdefghij', 'now', 'today', '2025-01-03T10:30:45 ',
            ' 2025-01-03', '2025-01-03 ', '2025-01-03\n', '2025-01-03T10:30:45\n', '\t2025-01-03',
            '2025-01-03T10:30:45Z\x00', '2025-01-03T10:30:45\x00', '2025-01-03T10:30:45+05:30\x00',
            '2025-01-03T10\x00', '2025-01-03T\x00', '2025-01-03\x00', '2025-01-03\x0010:30',
            '\x002025-01-03', '2025\x00-01-03', '2025-01-03T10:30:45.123\x00456']
    # Digits of other scripts, full-width digits, and look-alikes.
    out += ['٢٠٢٥-٠١-٠٣', '２０２５-０１-０３', '2025-٠١-03', '2025-01-٠٣', '2025-01-03T١٠:30', '2025-01-03T10:٣٠',
            '2025-01-03T10:30:45.١٢٣', '2025-01-03T10:30+٠٥:٣٠', '𝟚025-01-03', '2025-01-03T𝟙0:30', '2025−01−03',
            '2025-01-03T10:30:45 ', '2025-01-03 10:30', '2025-01-03　', '2025‑01‑03',
            '2025-01-03T10：30', '2025-01-03T10:30:45。5', '2025-01-03Ｔ10:30', '2025-01-03T10:30:45Ｚ',
            '2025-01-03T10:30:45z', '2025-01-03t10:30:45', '2025-01-03t10:30:45z', '2025-01-03T10:30:45\ud800',
            '\ud8002025-01-03', '2025-01-03\ud80010:30', '2025-01-03\udc0010:30', '2025-01-03T10:30\udfff']
    # Random edits of valid text.
    base = [s for s in out if s and not has_joinable_surrogates(s)]
    alphabet = list('0123456789-:.,TtZzW+ _x/') + ['٣', 'é', '😀', '\x00', '\ud800', '\udc00', ' ', '＋']
    valid = [s for s in base if _accepts(s)]
    for _ in range(2200):
        s = list(rng.choice(valid))
        for _ in range(rng.choice([1, 1, 1, 2, 3])):
            op = rng.choice(['del', 'ins', 'rep', 'dup'])
            i = rng.randrange(len(s) + 1)
            if op == 'del' and s:
                del s[min(i, len(s) - 1)]
            elif op == 'ins':
                s.insert(i, rng.choice(alphabet))
            elif op == 'rep' and s:
                s[min(i, len(s) - 1)] = rng.choice(alphabet)
            elif op == 'dup' and s:
                s.insert(i, s[min(i, len(s) - 1)])
        text = ''.join(s)
        if not has_joinable_surrogates(text):
            out.append(text)
    seen: set[str] = set()
    unique = []
    for s in out:
        if s not in seen and not has_joinable_surrogates(s):
            seen.add(s)
            unique.append(s)
    return unique


def _accepts(s: str) -> bool:
    try:
        datetime.fromisoformat(s)
        return True
    except ValueError:
        return False


def fromisoformat_cases(rng: random.Random) -> list[dict]:
    cases = []
    for s in iso_corpus(rng):
        body = result_or_error(lambda s=s: datetime.fromisoformat(s))
        cases.append({'s': T(s), **body})
    accepted = sum('r' in c for c in cases)
    rejected = len(cases) - accepted
    assert accepted >= 300 and rejected >= 150, (accepted, rejected)
    return cases


# ---- comparison ----

def compare_case(a: datetime, b: datetime) -> dict:
    case: dict = {'a': D(a), 'b': D(b), 'eq': a == b, 'ne': a != b}
    try:
        case['ord'] = [a < b, a <= b, a > b, a >= b]
    except TypeError as e:
        case['ord_err'] = T(str(e))
    return case


def same_instant(dt: datetime, off: int | None, rng: random.Random) -> datetime | None:
    """A value that is equal to dt, or that has the same clock fields, with another offset."""
    if off is None:
        return dt.replace(tzinfo=None)
    if dt.tzinfo is None:
        return dt.replace(tzinfo=timezone(timedelta(microseconds=off)))
    try:
        return dt.astimezone(timezone(timedelta(microseconds=off)))
    except OverflowError:
        return None


def compare_cases(rng: random.Random) -> list[dict]:
    cases = []
    seen: set[tuple] = set()

    def add(a: datetime, b: datetime) -> None:
        key = (tuple(D(a)), tuple(D(b)))
        if key not in seen:
            seen.add(key)
            cases.append(compare_case(a, b))

    small = small_pool(rng, 34)
    for a in small:
        for b in small:
            add(a, b)
    for _ in range(1500):
        a = rng.choice(POOL)
        roll = rng.random()
        if roll < 0.35:
            b = same_instant(a, rng.choice(OFFSETS), rng)
            if b is None:
                continue
        elif roll < 0.5:
            try:
                b = a + timedelta(microseconds=rng.choice([-1, 1, 0]))
            except OverflowError:
                continue
        else:
            b = rng.choice(POOL)
        add(a, b)
    return cases


# ---- UTC ----

def utc_cases(rng: random.Random) -> dict:
    pool = small_pool(rng, 150)
    to_utc = []
    for dt in pool + [mk(1, 1, 1, 0, 0, 0, 0, 1), mk(1, 1, 1, 0, 0, 0, 0, 0), mk(1, 1, 1, 0, 0, 0, 0, -1), mk(1, 1, 1, 5, 0, 0, 0, 5 * 3600 * SECOND),
                      mk(1, 1, 1, 4, 59, 59, 999999, 5 * 3600 * SECOND), mk(9999, 12, 31, 23, 59, 59, 999999, -1), mk(9999, 12, 31, 23, 59, 59, 999999, 1),
                      mk(9999, 12, 31, 16, 0, 0, 0, -8 * 3600 * SECOND), mk(9999, 12, 31, 15, 59, 59, 999999, -8 * 3600 * SECOND)]:
        if dt.tzinfo is None:
            continue
        to_utc.append({'dt': D(dt), **result_or_error(lambda dt=dt: dt.astimezone(UTC))})
    return {
        'to_utc': to_utc,
        'replace_utc': [{'dt': D(dt), 'r': D(dt.replace(tzinfo=UTC))} for dt in pool],
        'flags': [{'dt': D(dt), 'naive': dt.tzinfo is None, 'utc': dt.tzinfo == UTC} for dt in pool],
    }


# ---- DateTimeOffset and the clock ----

EPOCH = datetime(1970, 1, 1)


def dto_cases(rng: random.Random) -> dict:
    """neo4j.time.DateTime(...).to_native() is the call of upstream's parse_db_date. It cuts nanoseconds to microseconds."""
    from_dto = []
    fractions = [0, 1, 9, 10, 11, 99, 100, 999999, 1000000, 1234567, 9999999, 5, 15, 9999990, 9999991]
    for _ in range(120):
        y, mo, d = rng.choice(DATES)
        h, mi, s, _us = rng.choice(TIMES)
        ticks = rng.choice(fractions + [rng.randrange(10_000_000)])
        off_min = rng.choice([0, 0, 330, -480, 60, 840, -840, -720, 1, -1])
        try:
            datetime(y, mo, d, h, mi, s) - timedelta(minutes=off_min)
        except OverflowError:
            continue  # A DateTimeOffset cannot hold an instant outside year 1 to 9999.
        native = Neo4jDateTime(y, mo, d, h, mi, s, ticks * 100, timezone(timedelta(minutes=off_min))).to_native()
        from_dto.append({'f': [y, mo, d, h, mi, s, ticks, off_min], 'r': D(native)})
    # NOTE: Python has no counterpart of DateTimeOffset. The expected values of to_dto are NOT recorded from Python.
    # They follow decision D36 (a naive value is read as UTC) and the rule that an offset that DateTimeOffset cannot
    # hold (seconds, or beyond 14 hours) becomes UTC at the same instant. Python only gives the UTC instant (datetime
    # arithmetic) and the OverflowError at the ends of the range.
    to_dto = []
    limit_offsets = [14 * 3600, 14 * 3600 + 60, -14 * 3600, -(14 * 3600 + 60)]  # the limit of DateTimeOffset and one minute beyond
    for dt in small_pool(rng, 140) + [mk(2025, 1, 3, 10, 0, 0, 0, o * SECOND) for o in limit_offsets]:
        base = dt.replace(tzinfo=None)
        off = off_us(dt) or 0
        representable = dt.tzinfo is None or (off % (60 * SECOND) == 0 and abs(off) <= 14 * 3600 * SECOND)
        # A naive value is read as UTC (D36). An offset that DateTimeOffset cannot hold becomes UTC at the same instant.
        try:
            instant = base - timedelta(microseconds=off)
            utc_micros = (instant - EPOCH) // US
        except OverflowError as e:
            to_dto.append({'dt': D(dt), **failure(e)})
            continue
        to_dto.append({'dt': D(dt), 'utc_micros': utc_micros, 'off_min': (off // (60 * SECOND)) if representable and dt.tzinfo is not None else 0})
    # A TimeProvider can return an instant with any offset and with ticks below the microsecond. UtcNow gives UTC.
    utc_now = []
    for _ in range(120):
        y, mo, d = rng.choice(DATES)
        h, mi, s, _us = rng.choice(TIMES)
        ticks = rng.choice(fractions + [rng.randrange(10_000_000)])
        off_min = rng.choice([0, 0, 330, -480, 60, 840, -840, -720, 1, -1])
        try:
            utc = datetime(y, mo, d, h, mi, s) - timedelta(minutes=off_min)
        except OverflowError:
            continue
        native = Neo4jDateTime(utc.year, utc.month, utc.day, utc.hour, utc.minute, utc.second, ticks * 100, UTC).to_native()
        utc_now.append({'f': [y, mo, d, h, mi, s, ticks, off_min], 'r': D(native)})
    return {'from_dto': from_dto, 'to_dto': to_dto, 'utc_now': utc_now}


def build_values() -> dict:
    rng = random.Random(SEED)
    cases: dict = {}
    cases.update(text_cases())
    cases['from_iso_format'] = fromisoformat_cases(rng)
    cases['compare'] = compare_cases(rng)
    cases.update(utc_cases(rng))
    cases.update(dto_cases(rng))
    return {
        'meta': {'python': sys.version.split()[0], 'seed': SEED,
                 'note': 'Generated by port/tools/generate_datetime_values.py. Do not edit.'},
        'cases': cases,
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=False) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_datetime_values: up to date')
        return 0
    VALUES_PATH.parent.mkdir(parents=True, exist_ok=True)
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
