"""Records results of graphiti_core/tracer.py from real Python, for the evaluation tests.

The judge has no case for this module, and upstream has no test for it. The module holds the two things that a
reader gets wrong: the filter of the attributes of a span, and the end of start_span with an OpenTelemetry tracer
(upstream defect U19, port/GAPS.md). This file runs generated spans from a fixed seed (port/RULES.md, R23 and R25)
against the real OpenTelemetry SDK with an in-memory exporter, so that the port can be compared with each result.

    .upstream/graphiti/.venv/bin/python port/tools/record_tracer_values.py
    .upstream/graphiti/.venv/bin/python port/tools/record_tracer_values.py --check

It writes tests/Port.Evaluation/values/tracer.json. --check writes nothing and exits 1 if the file differs from what
the run would write. The tool needs Python 3.12, the pinned upstream in .upstream/graphiti, and opentelemetry-sdk in
the virtual environment of the upstream. It changes nothing there. Nothing here uses the network.

Each case holds the tracer kind, the span prefix, the span name, and what the body of the span does: the attributes
that it adds, the status calls that it makes, whether it records an exception, and how it ends. The result holds
how the `with` ended and what the exporter saw: for each span the name, the attributes, the status and its
description, and for each event its name, the sorted keys of its attributes, `exception.type`, and
`exception.message`. The attribute values cover the value model of port/RULES.md (R6): None, bool, int (also
beyond 64 bits), float, str, list, tuple, dict, bytes, date, and datetime. A value that is not JSON is a dict with the
key `$`. The attribute keys include the empty key, which the SDK drops. The bodies raise ValueError, RuntimeError,
KeyError, and NodeNotFoundError (graphiti_core.errors), or are cancelled. One tracer kind has a span processor that
raises at the end of the span (the branch `generator didn't stop`).

The recording leaves out the context of an exception of the body after a start that failed: Python chains the
failure of the start to it, and a C# exception has no such link.

The test of the port does not rewrite what this file records. Decision D46 says that the telemetry of an exception
keeps its .NET identity, so the status description and `exception.type` differ from these values by the table
`D46Names` of the test, and the event has no key `exception.escaped`.
"""
from __future__ import annotations

import asyncio
import datetime
import json
import logging
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', ROOT / '.upstream' / 'graphiti'))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'tracer.json'

PREFIXES = ['graphiti', 'graphiti.', 'myapp.graphiti', 'a...', '', '.', '..x', ' ', 'p. ', 'é.', 'graphiti.x.',
            '.a.', 'ZEP..', '日本.']
NAMES = ['search.edge_search', 'op', '', 'a b', 'x.y.z', 'é', '.', 'Name', 'add_episode', '..']
KEYS = ['a', 'count', 'search.query', 'k k', 'é', 'x.y', 'None', 'K', '', ' ']
STRINGS = ['', 'text', 'é', 'a\nb', '{"x": 1}', 'True', '日本', '"q"', "it's"]
STATUS_CALLS = [['ok', None], ['ok', 'fine'], ['error', 'bad'], ['error', None], ['error', ''], ['unknown', 'x'],
                ['OK', None], ['', None], ['Error', 'x']]
MESSAGES = ['boom', 'x', '', 'two words', 'é', "it's", '日本']
BODIES = ['returns', 'returns', 'returns', 'value_error', 'value_error', 'runtime_error', 'key_error', 'node_not_found',
          'cancelled']
RECORDS = [None, None, None, 'ValueError', 'KeyError']

rng = random.Random(20261127)
records: list[dict] = []


def rand_special():
    """A value of the value model that JSON cannot hold, as a dict with the key `$`."""
    r = rng.random()
    if r < 0.3:
        return {'$': 'tuple', 'items': [rand_value(2) for _ in range(rng.randint(0, 3))]}
    if r < 0.5:
        return {'$': 'bytes', 'hex': bytes(rng.randint(0, 255) for _ in range(rng.randint(0, 4))).hex()}
    if r < 0.7:
        return {'$': 'date', 'y': rng.choice([1, 1999, 2024]), 'mo': rng.randint(1, 12), 'd': rng.randint(1, 28)}
    return {
        '$': 'datetime',
        'y': rng.choice([1, 1999, 2024]),
        'mo': rng.randint(1, 12),
        'd': rng.randint(1, 28),
        'h': rng.randint(0, 23),
        'mi': rng.randint(0, 59),
        's': rng.randint(0, 59),
        'us': rng.choice([0, 0, 1, 123456, 999999]),
        'off': rng.choice([None, 0, 330, -90, 840]),
    }


def decode(value):
    """The Python value of a JSON form with the key `$`."""
    if isinstance(value, list):
        return [decode(item) for item in value]
    if isinstance(value, dict):
        kind = value.get('$')
        if kind == 'tuple':
            return tuple(decode(item) for item in value['items'])
        if kind == 'bytes':
            return bytes.fromhex(value['hex'])
        if kind == 'date':
            return datetime.date(value['y'], value['mo'], value['d'])
        if kind == 'datetime':
            zone = None if value['off'] is None else datetime.timezone(datetime.timedelta(minutes=value['off']))
            return datetime.datetime(value['y'], value['mo'], value['d'], value['h'], value['mi'], value['s'], value['us'], tzinfo=zone)
        return {key: decode(item) for key, item in value.items()}
    return value


def rand_value(depth: int = 0):
    r = rng.random()
    if r < 0.15:
        return None
    if r < 0.3:
        return rng.choice([True, False])
    if r < 0.5:
        return rng.randint(-1000, 1000)
    if r < 0.6:
        return rng.choice([0, 1, -1, 2**31, -(2**31), 2**53, 2**63 - 1, -(2**63), 2**63, -(2**63) - 1, 2**64, 2**70,
                           -(2**100)])
    if r < 0.75:
        return rng.choice([0.0, -0.0, 1.5, 0.1, 1e16, 1e-7, 123456789.123, 1e22, 5e-324, -2.5])
    if r < 0.85 or depth >= 2:
        return rng.choice(STRINGS)
    if r < 0.9:
        return [rand_value(depth + 1) for _ in range(rng.randint(0, 3))]
    if r < 0.95:
        return rand_special()
    return {rng.choice(KEYS[:8]): rand_value(depth + 1) for _ in range(rng.randint(0, 3))}


def rand_case() -> dict:
    tracer = rng.choice(['otel', 'otel', 'otel', 'noop', 'otel_fail_start', 'otel_fail_end'])
    attrs = None
    if rng.random() < 0.7:
        attrs = {}
        for _ in range(rng.randint(0, 5)):
            attrs[rng.choice(KEYS)] = rand_value()
    return {
        'tracer': tracer,
        'prefix': rng.choice(PREFIXES),
        'name': rng.choice(NAMES),
        'body': rng.choice(BODIES),
        'msg': rng.choice(MESSAGES),
        'ret': rng.choice([None, 5, 'done', 1.5, True]),
        'attrs': attrs,
        'status': [rng.choice(STATUS_CALLS) for _ in range(rng.choice([0, 0, 1, 1, 2, 3]))],
        'record': rng.choice(RECORDS),
    }


def run_case(case: dict, tracers, exporter, errors) -> dict:
    exporter.clear()
    kind = case['tracer']
    if kind == 'noop':
        tracer = tracers.NoOpTracer()
    elif kind == 'otel':
        tracer = tracers.create_tracer(case['_provider'].get_tracer('x'), case['prefix'])
    elif kind == 'otel_fail_end':
        tracer = tracers.create_tracer(case['_failing_end_provider'].get_tracer('x'), case['prefix'])
    else:
        class FailingTracer:
            def start_as_current_span(self, name):
                raise RuntimeError('cannot start')

        tracer = tracers.create_tracer(FailingTracer(), case['prefix'])

    def body(span):
        if case['attrs'] is not None:
            span.add_attributes(decode(case['attrs']))
        for status, description in case['status']:
            span.set_status(status, description)
        if case['record'] == 'ValueError':
            span.record_exception(ValueError(case['msg']))
        if case['record'] == 'KeyError':
            span.record_exception(KeyError(case['msg']))
        if case['body'] == 'value_error':
            raise ValueError(case['msg'])
        if case['body'] == 'runtime_error':
            raise RuntimeError(case['msg'])
        if case['body'] == 'key_error':
            raise KeyError(case['msg'])
        if case['body'] == 'node_not_found':
            raise errors.NodeNotFoundError(case['msg'])
        if case['body'] == 'cancelled':
            raise asyncio.CancelledError()
        return case['ret']

    seen = {'span': None}
    try:
        with tracer.start_span(case['name']) as span:
            seen['span'] = type(span).__name__
            value = body(span)
        outcome = {'ended': 'returned', 'value': value}
    except BaseException as error:
        context = None if kind == 'otel_fail_start' else error.__context__
        outcome = {
            'ended': 'raised',
            'type': type(error).__name__,
            'message': str(error),
            'context': None if context is None else [type(context).__name__, str(context)],
        }
    outcome['span_class'] = seen['span']
    outcome['spans'] = [
        {
            'name': s.name,
            'attributes': dict(s.attributes),
            'status': s.status.status_code.name,
            'description': s.status.description,
            'events': [
                [e.name, sorted(e.attributes), e.attributes.get('exception.type'), e.attributes.get('exception.message')]
                for e in s.events
            ],
        }
        for s in exporter.get_finished_spans()
    ]
    return outcome


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    from opentelemetry.sdk.trace import SpanProcessor, TracerProvider
    from opentelemetry.sdk.trace.export import SimpleSpanProcessor
    from opentelemetry.sdk.trace.export.in_memory_span_exporter import InMemorySpanExporter

    from graphiti_core import errors
    from graphiti_core import tracer as tracers

    class FailingEnd(SpanProcessor):
        def on_end(self, span):
            raise ValueError('end failed')

    exporter = InMemorySpanExporter()
    provider = TracerProvider()
    provider.add_span_processor(SimpleSpanProcessor(exporter))
    # The processor that raises comes first, so the exporter of this provider never sees a span.
    failing_end_provider = TracerProvider()
    failing_end_provider.add_span_processor(FailingEnd())
    failing_end_provider.add_span_processor(SimpleSpanProcessor(exporter))
    for _ in range(900):
        case = rand_case()
        case['_provider'] = provider
        case['_failing_end_provider'] = failing_end_provider
        result = run_case(case, tracers, exporter, errors)
        del case['_provider']
        del case['_failing_end_provider']
        records.append({'c': case, 'r': result})

    lines = [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","source":"graphiti_core/tracer.py","cases":[\n'
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
