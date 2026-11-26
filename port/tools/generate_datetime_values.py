"""Records what real Python gives for the PyDateTime helper of PyCompat (plan 0003, task P4).

One file comes out of one run:

- tests/Graphiti.Net.Property.Tests/python-values.datetime.json: inputs and the Python result of each one.
  The property tests in DateTimeTests.cs read it. A result is a value or the name and the message of a Python error.

    .upstream/graphiti/.venv/bin/python port/tools/generate_datetime_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_datetime_values.py --check

--check writes nothing and exits 1 if the file differs from what the run would write.
The tool needs Python 3.12, the version that RULES.md names for the pinned upstream.
Strings are lists of code points, because JSON text cannot carry a lone surrogate.
A datetime is a list [year, month, day, hour, minute, second, microsecond, offset], where the offset is the
utcoffset in microseconds and null for a naive value.
The random cases use a fixed seed. Nothing here uses the network.

Two facts that the cases keep away from, because Python does not give one answer for them:

- strftime('%Y') for a year below 1000 depends on the C library (glibc writes no padding, macOS writes 4 digits).
  The cases of %Y use years from 1000.
- A format that holds a directive twice makes the re module raise re.error, which is not a ValueError. No case has one.
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
    return {'e': type(e).__name__, 'msg': cps(str(e))}


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
        'iso_format': [{'dt': D(dt), 'r': cps(dt.isoformat())} for dt in POOL],
        'str': [{'dt': D(dt), 'r': cps(str(dt))} for dt in POOL],
        'repr': [{'dt': D(dt), 'r': cps(repr(dt))} for dt in POOL],
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
        cases.append({'s': cps(s), **body})
    accepted = sum('r' in c for c in cases)
    rejected = len(cases) - accepted
    assert accepted >= 300 and rejected >= 150, (accepted, rejected)
    return cases


# ---- strftime ----

STRFTIME_FORMATS = [
    '%Y', '%m', '%d', '%H', '%M', '%S', '%f', '%z', '%j', '%y', '%b', '%B', '%a', '%A', '%p', '%I', '%%', '',
    'plain text', '%Y-%m-%d', '%Y-%m-%dT%H:%M:%S', '%Y-%m-%dT%H:%M:%S.%f%z', '%a, %d %b %Y %H:%M:%S %z',
    '%A %B %j %I:%M %p', '%d/%m/%y %I:%M:%S %p', '%%Y %%', '%%%Y', 'a%', '%H%M%S%f', 'é%Y😀', 'é %m',
    '%Y %Y', '%d%d', '%b %b', '%I%I%p%p', '100%% sure', '  %H  ', '\t%S\n', '%Yé%m', '%y%y%y',
]


def strftime_cases(rng: random.Random) -> list[dict]:
    cases = []
    for dt in small_pool(rng, 60):
        for fmt in STRFTIME_FORMATS:
            if '%Y' in fmt.replace('%%', '') and dt.year < 1000:
                continue
            cases.append({'dt': D(dt), 'f': cps(fmt), 'r': cps(dt.strftime(fmt))})
    # Every hour for %I and %p, every month and weekday for the names, and the day of the year around a leap day.
    for hour in range(24):
        dt = datetime(2025, 3, 4, hour, 5, 6)
        cases.append({'dt': D(dt), 'f': cps('%H %I %p'), 'r': cps(dt.strftime('%H %I %p'))})
    for month in range(1, 13):
        dt = datetime(2024, month, 28, 1, 2, 3)
        cases.append({'dt': D(dt), 'f': cps('%m %b %B %j'), 'r': cps(dt.strftime('%m %b %B %j'))})
    for day in range(7):
        dt = datetime(2025, 1, 6 + day)
        cases.append({'dt': D(dt), 'f': cps('%a %A %d'), 'r': cps(dt.strftime('%a %A %d'))})
    for dt in [datetime(2024, 12, 31), datetime(2023, 12, 31), datetime(2024, 3, 1), datetime(2023, 3, 1),
               datetime(9999, 12, 31), datetime(2000, 1, 1), datetime(1999, 1, 1), datetime(2024, 1, 1)]:
        cases.append({'dt': D(dt), 'f': cps('%j %y %Y'), 'r': cps(dt.strftime('%j %y %Y'))})
    return cases


# ---- strptime ----

def strptime_case(text: str, fmt: str) -> dict:
    try:
        r = datetime.strptime(text, fmt)
        return {'s': cps(text), 'f': cps(fmt), 'r': D(r)}
    except ValueError as e:
        return {'s': cps(text), 'f': cps(fmt), **failure(e)}


HAND_STRPTIME = [
    # whitespace
    ('2025-01-03   10:30', '%Y-%m-%d %H:%M'), ('2025-01-03\t10:30', '%Y-%m-%d %H:%M'), ('2025-01-0310:30', '%Y-%m-%d %H:%M'),
    ('2025-01-03 10:30', '%Y-%m-%d  %H:%M'), (' 2025-01-03', '%Y-%m-%d'), ('2025-01-03 ', '%Y-%m-%d'),
    ('2025-01-03 10:30', '%Y-%m-%d %H:%M'), ('2025-01-03　 10:30', '%Y-%m-%d %H:%M'),
    ('2025-01-03\x1c10:30', '%Y-%m-%d %H:%M'), ('2025-01-03\n10:30', '%Y-%m-%d\n%H:%M'),
    # case
    ('JAN 03 2025', '%b %d %Y'), ('jan 03 2025', '%b %d %Y'), ('MARCH 3 2025', '%B %d %Y'), ('mon jan 6 2025', '%a %b %d %Y'),
    ('SUNDAY', '%A'), ('03 pm', '%I %p'), ('03 PM', '%I %p'), ('03 Pm', '%I %p'), ('3 xm', '%I %p'), ('2025-01-03t10', '%Y-%m-%dT%H'),
    ('ſep 03 2025', '%b %d %Y'), ('sep 03 2025', '%b %d %Y'), ('Kan 03 2025', '%b %d %Y'), ('maY 03 2025', '%b %d %Y'),
    ('İan 03 2025', '%b %d %Y'), ('ıan 03 2025', '%b %d %Y'), ('Dec 03 2025', '%b %d %Y'), ('Décembre 03 2025', '%B %d %Y'),
    ('may 3 2025', '%B %d %Y'), ('Sept 03 2025', '%b %d %Y'), ('Monday', '%a'), ('Mon', '%A'), ('wed', '%a'), ('Wednesday', '%a'),
    # names and the order of the alternatives
    ('March', '%b'), ('Mar', '%B'), ('Marc', '%B'), ('JUNE', '%b'), ('Jun', '%b'), ('Tues', '%a'), ('Tue', '%a'),
    # 12-hour clock
    ('12 AM', '%I %p'), ('12 PM', '%I %p'), ('1 AM', '%I %p'), ('11 PM', '%I %p'), ('13 PM', '%I %p'), ('00 AM', '%I %p'),
    ('12', '%I'), ('12 30', '%I %M'), (' 3 pm', '%I %p'), ('PM 3', '%p %I'), ('3 PM 10', '%I %p %H'), ('10 3 PM', '%H %I %p'),
    # two-digit years
    ('68', '%y'), ('69', '%y'), ('00', '%y'), ('99', '%y'), ('٦٨', '%y'), ('6', '%y'), ('100', '%y'),
    # years
    ('0001', '%Y'), ('9999', '%Y'), ('0000', '%Y'), ('10000', '%Y'), ('999', '%Y'), ('٢٠٢٥', '%Y'), ('２０２５-0１-0３', '%Y-%m-%d'),
    ('-2025', '%Y'), ('+2025', '%Y'), ('20 25', '%Y'),
    # month and day limits
    ('2024-02-29', '%Y-%m-%d'), ('2023-02-29', '%Y-%m-%d'), ('1900-02-29', '%Y-%m-%d'), ('2000-02-29', '%Y-%m-%d'),
    ('2024-04-31', '%Y-%m-%d'), ('2024-4-5', '%Y-%m-%d'), ('2024-04-05', '%Y-%m-%d'), ('2024-13-05', '%Y-%m-%d'),
    ('2024-00-05', '%Y-%m-%d'), ('2024-12-00', '%Y-%m-%d'), ('2024-12-32', '%Y-%m-%d'), ('2024-12- 5', '%Y-%m-%d'),
    ('2024-12-٠٥', '%Y-%m-%d'), ('02-29', '%m-%d'), ('02-28', '%m-%d'), ('2-29', '%m-%d'), ('12-31', '%m-%d'), ('04-31', '%m-%d'),
    ('29 February', '%d %B'), ('29 February 2023', '%d %B %Y'), ('29 February 2024', '%d %B %Y'),
    # day of the year
    ('001 2025', '%j %Y'), ('1 2025', '%j %Y'), ('059 2025', '%j %Y'), ('060 2025', '%j %Y'), ('060 2024', '%j %Y'),
    ('365 2025', '%j %Y'), ('366 2025', '%j %Y'), ('366 2024', '%j %Y'), ('367 2025', '%j %Y'), ('000 2025', '%j %Y'),
    ('366 9999', '%j %Y'), ('365 9999', '%j %Y'), ('1 0001', '%j %Y'), ('1 0000', '%j %Y'), ('45', '%j'), ('366', '%j'),
    ('2025-02-30 060', '%Y-%m-%d %j'), ('2025-02-30 001', '%Y-%m-%d %j'), ('2025 6 2', '%Y %m %j'), ('2025-045', '%Y-%j'),
    ('٠٤٥ 2025', '%j %Y'), ('3 2025', '%j %Y'), ('36 2025', '%j %Y'), ('360 2025', '%j %Y'), ('369 2025', '%j %Y'),
    # time limits
    ('23:59:59', '%H:%M:%S'), ('24:00:00', '%H:%M:%S'), ('23:60:00', '%H:%M:%S'), ('23:59:60', '%H:%M:%S'), ('23:59:61', '%H:%M:%S'),
    ('23:59:62', '%H:%M:%S'), ('0:0:0', '%H:%M:%S'), ('00:00:00', '%H:%M:%S'), ('7:5:3', '%H:%M:%S'), ('٠٧:٠٥:٠٣', '%H:%M:%S'),
    ('1:2', '%H:%M'), ('12:5', '%H:%M'), ('2:30', '%I:%M'), ('24', '%H'), ('9', '%H'), ('95', '%M'), ('5', '%M'), ('60', '%S'), ('61', '%S'),
    # fractions
    ('1', '%f'), ('12', '%f'), ('123456', '%f'), ('1234567', '%f'), ('000001', '%f'), ('999999', '%f'), ('0', '%f'),
    ('٠١٢', '%f'), ('12:30:15.5', '%H:%M:%S.%f'), ('12:30:15.', '%H:%M:%S.%f'), ('12:30:15', '%H:%M:%S.%f'), ('.5', '.%f'),
    # offsets
    ('Z', '%z'), ('z', '%z'), ('+00:00', '%z'), ('-00:00', '%z'), ('+0000', '%z'), ('+05:30', '%z'), ('+0530', '%z'), ('-08:00', '%z'),
    ('+05:30:15', '%z'), ('+053015', '%z'), ('+05:30:15.5', '%z'), ('+05:30:15.123456', '%z'), ('+053015.123456', '%z'),
    ('+05:30:15.1234567', '%z'), ('-05:30:15.5', '%z'), ('-00:00:00.5', '%z'), ('+00:00:00.5', '%z'), ('+00:00:30', '%z'),
    ('+24:00', '%z'), ('-24:00', '%z'), ('+23:59:59.999999', '%z'), ('-23:59:59.999999', '%z'), ('+99:59', '%z'), ('+00:60', '%z'),
    ('+05', '%z'), ('+5:30', '%z'), ('+05:3', '%z'), ('+0530:15', '%z'), ('+05:3015', '%z'), ('+05:30:1', '%z'), ('+05:30:15:5', '%z'),
    ('+٠٥:٣٠', '%z'), ('+05:30 ', '%z'), (' +05:30', '%z'), ('+05:30Z', '%z'), ('ZZ', '%z'), ('2025-01-03 +0530', '%Y-%m-%d %z'),
    ('2025-01-03 Z', '%Y-%m-%d %z'), ('2025-01-03 10:30:00Z', '%Y-%m-%d %H:%M:%S%z'),
    ('2025-01-03T10:30:00.123456+05:30', '%Y-%m-%dT%H:%M:%S.%f%z'), ('2025-01-03T10:30:00.123456-0800', '%Y-%m-%dT%H:%M:%S.%f%z'),
    # percent signs
    ('50%', '%d%%'), ('50', '%d%%'), ('%50', '%%%d'), ('5%', '%d%'), ('5 ', '%d%'), ('%', '%'), ('%', '%%'), ('%%', '%%'),
    # literals that the pattern changes
    ("5 o'clock", "%d o'clock"), ('5 oʼclock', "%d o'clock"), ("5 o'clock", '%d oʼclock'), ('5.3', '%d.%m'), ('5x3', '%d.%m'),
    ('5(3)', '%d(%m)'), ('5[3]', '%d[%m]'), ('5{3}', '%d{%m}'), ('5|3', '%d|%m'), ('5*3', '%d*%m'), ('5+3', '%d+%m'), ('5?3', '%d?%m'),
    ('5^3', '%d^%m'), ('5$3', '%d$%m'), ('5\\3', '%d\\%m'), ('5-3', '%d-%m'), ('5#3', '%d#%m'), ('5é3', '%dé%m'), ('5É3', '%dé%m'),
    ('5😀3', '%d😀%m'), ('5é3', '%dé%m'), ('5Σ3', '%dσ%m'), ('5ς3', '%dσ%m'), ('5K3', '%dK%m'), ('5K3', '%dk%m'),
    ('5ſ3', '%ds%m'), ('5ı 3', '%di %m'), ('5I3', '%di%m'), ('5İ3', '%di%m'),
    # data that is longer or shorter
    ('2025-01-03 extra', '%Y-%m-%d'), ('2025-01-03', '%Y-%m-%d extra'), ('', '%Y'), ('', ''), ('x', ''), ('2025', ''), ('2025-01', '%Y-%m-%d'),
    ('20250103', '%Y%m%d'), ('2025013', '%Y%m%d'), ('20251301', '%Y%m%d'), ('202501031', '%Y%m%d'), ('2025-01-03', '%Y%m%d'),
    ('1', '%d'), ('12', '%d'), ('31', '%d'), ('32', '%d'), ('3 2', '%d %m'), (' 5', '%d'), ('05', '%d'), ('5', '%d'), ('0', '%d'),
    ('2025\x00', '%Y'), ('2025\x00', '%Y\x00'), ('20\ud80025', '%Y'), ('\ud800', '\ud800'),
]

BAD_FORMATS = [
    '%Q', '%q', '%-d', '%e', '%k', '%l', '%s', '%n', '%t', '% d', '%.', '%(', '%[', '%\\', "%'", '%O', '%Oz', '%Ox', '%OQ', '%',
    'a %', '%%%', '%é', '%😀', '%\n', '% ', 'x%Q', '%Y%Q', '%d %!', '%E', '%EY', '%C', '%D', '%F', '%g', '%h', '%r', '%R', '%T', '%v',
]


def strptime_cases(rng: random.Random) -> list[dict]:
    cases = [strptime_case(s, f) for s, f in HAND_STRPTIME]
    for fmt in BAD_FORMATS:
        cases.append(strptime_case('2025', fmt))
    # The text that strftime writes for a datetime reads back, when the format holds the fields.
    round_trip = ['%Y-%m-%d %H:%M:%S.%f', '%Y-%m-%dT%H:%M:%S.%f%z', '%a, %d %b %Y %H:%M:%S', '%A %B %d %Y %I:%M %p',
                  '%y%m%d%H%M%S', '%j %Y %H', '%d/%m/%y', '%Y-%m-%d %H:%M:%S %z']
    for dt in small_pool(rng, 40):
        for fmt in round_trip:
            if dt.year < 1000 or (dt.utcoffset() is not None and '%z' not in fmt):
                continue
            cases.append(strptime_case(dt.strftime(fmt), fmt))
    # Random edits of text that parses.
    alphabet = list('0123456789-:.,/ +ZzTtAaPpMmJjFfSsOoNnDdEe') + ['٣', 'é', '😀', '\x00', ' ', 'ſ', 'K']
    seeds = [c for c in cases if 'r' in c]
    for _ in range(1800):
        base = rng.choice(seeds)
        s = list(''.join(map(chr, base['s'])))
        for _ in range(rng.choice([1, 1, 2, 3])):
            op = rng.choice(['del', 'ins', 'rep'])
            i = rng.randrange(len(s) + 1)
            if op == 'del' and s:
                del s[min(i, len(s) - 1)]
            elif op == 'ins':
                s.insert(i, rng.choice(alphabet))
            elif op == 'rep' and s:
                s[min(i, len(s) - 1)] = rng.choice(alphabet)
        text = ''.join(s)
        fmt = ''.join(map(chr, base['f']))
        if not has_joinable_surrogates(text):
            cases.append(strptime_case(text, fmt))
    return cases


# ---- comparison and subtraction ----

def compare_case(a: datetime, b: datetime) -> dict:
    case: dict = {'a': D(a), 'b': D(b), 'eq': a == b, 'ne': a != b}
    try:
        case['ord'] = [a < b, a <= b, a > b, a >= b]
    except TypeError as e:
        case['ord_err'] = cps(str(e))
    try:
        case['sub'] = (a - b) // US
    except TypeError as e:
        case['sub_err'] = cps(str(e))
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
    small = small_pool(rng, 34)
    for a in small:
        for b in small:
            cases.append(compare_case(a, b))
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
        cases.append(compare_case(a, b))
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
    to_dto = []
    for dt in small_pool(rng, 140):
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
    return {'from_dto': from_dto, 'to_dto': to_dto}


def build_values() -> dict:
    rng = random.Random(SEED)
    cases: dict = {}
    cases.update(text_cases())
    cases['from_iso_format'] = fromisoformat_cases(rng)
    cases['strftime'] = strftime_cases(rng)
    cases['strptime'] = strptime_cases(rng)
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
    text = json.dumps(build_values(), separators=(',', ':'), ensure_ascii=True) + '\n'
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
