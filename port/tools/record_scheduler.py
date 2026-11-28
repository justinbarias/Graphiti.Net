"""Records results of graphiti_core/ingestion/scheduler.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_scheduler.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_scheduler.py --check

Plan 0004, layer 8, packet P8d. The tool runs each path of ConcurrentIngestor that a prepared state store, a fake clock,
and prepared units can reach with no method of `Graphiti`:

- __init__ with a given store and with no store, _group, _GroupState.notify, and close (the shutdown),
- the wait function of _wait_local: the wake-up by notify, the poll interval, a poll interval on the clock, the
  smallest positive poll interval, the event that the call reads, and a cancel of the waiter,
- _heartbeat (the lease): a renewal of the deadline, a store error (the warning log line), an expired unit, a failed
  unit, a deadline on the clock, a stale fence, and the stale turn that UnitLease.heartbeat ignores,
- _run_unit up to the end of the Analyze phase: an analysis that fails, a unit that expires before the analysis or
  during it, a store error in _fail_unit, a cancel of the caller, two units of one graph and a waiter that the end of
  a unit wakes,
- add_episode and add_episode_bulk up to the admit lock: the window (more calls than the window, a window of one, a
  window on its limit, two graphs), the span of each call, and a cancel of a call that waits for the window or for the
  admit lock.

Paths that wait for layer 9: after the admit lock, add_episode and add_episode_bulk call admit_episode and admit_bulk,
and an analysis that returns calls commit_unit and enrich_unit. Each one calls graphiti._resolve_request_scope first,
and GraphitiCore is a skeleton until layer 9. So the wait for a commit turn, the order of the commits, and the retry
of an apply wait for layer 9 here. The recording of phases.py (P8c) covers the parts of them that need no `Graphiti`.
The `graphiti` of this tool has a `tracer` and a `_resolve_request_scope` that raises AssertionError, so a case that
reaches a skeleton member fails the recording.

The fakes:
- The loop has a virtual clock. It moves only at a `loop` step of a case, as the TimeProvider of the C# test does.
  asyncio.sleep and asyncio.wait_for use it. No case puts two timers on one time.
- The state store is the real InMemoryIngestionStateStore with a fake clock (FakeClock of the fixtures), inside a
  wrapper that yields once (asyncio.sleep(0)) and then records each call, as a store that does I/O does. A case can
  move the store clock at a named store call (`store_advance`), and the n-th call of a store method can fail
  (`store_fail`) or wait on the loop clock (`store_hold`). A held call that is canceled does cleanup work on the loop
  clock (`hold_cleanup` seconds), records `store_cleanup`, and raises the cancel again.
- The analysis of _run_unit is a fake that yields and records (`analyze`), waits for a gate, sleeps on the loop clock,
  marks the unit failed, and raises. It never returns: commit_unit waits for layer 9.
- The tracer records each span, its attributes, and its status, and counts the spans in flight.
- The calls, the store calls, the log lines, and the snapshots of each case are one ordered list. A snapshot holds the
  state of each call, `locked()` of the window and of the admit lock of each graph, and the spans. A log line has
  `exc: true` when the record has exc_info.
- For each counted fake call, `tasks` names the task that made it: the key of a call of the case, `<inner>` for the
  heartbeat task that _run_unit starts, or `<driver>` for the task of the case itself. The test checks the token of
  each fake call against it.

The failure reason that _fail_unit stores is type(error).__name__ in Python and the .NET type name in the port (decision
D50, R15). The test maps the .NET name to the Python name with the table of PhasesValuesTests.PythonName. The text of
a cancellation is empty. Real clock sites: _run_unit builds UnitLease with its default clock time.monotonic
(scheduler.py:136), which only sets the local deadline of the lease. No reachable path reads that deadline.

The cancel scenarios: for each case with `cancel`, the tool runs the case again for each counted fake call k. The fake
that makes call k records it and then cancels the task of the main call. The tool keeps the count of attempts, and the
counted calls and the log lines after call k (R25).

It writes tests/Port.Evaluation/values/scheduler.json. --check writes nothing and exits 1 if the file differs from what
the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable GRAPHITI_UPSTREAM,
or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import inspect
import json
import logging
import os
import sys
from datetime import datetime, timezone
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'scheduler.json'

T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)
DRAIN = 100
HOLD_SECONDS = 100.0

events: list = []
state: dict = {}


class FakeClock:
    def __init__(self, now: float = 1000.0):
        self.now = now

    def __call__(self) -> float:
        return self.now


class NoWaitSelector:
    """The selector of the virtual loop: it never waits, so the loop clock moves only at a `loop` step."""

    def __init__(self, inner):
        self.inner = inner
        self.idle = 0

    def select(self, timeout=None):
        if timeout is None or timeout > 0:
            self.idle += 1
            if self.idle > 1_000_000:
                raise RuntimeError('the virtual loop has no ready task and no step moves its clock')
        else:
            self.idle = 0
        return self.inner.select(0)

    def __getattr__(self, name):
        return getattr(self.inner, name)


class VirtualLoop(asyncio.SelectorEventLoop):
    def __init__(self):
        super().__init__()
        self.now = 1000.0
        self._selector = NoWaitSelector(self._selector)

    def time(self) -> float:
        return self.now


def encode(value):
    if isinstance(value, dict):
        return {str(k): encode(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    return value


def store_view(store) -> list:
    out = []
    for group_id, group in store._groups.items():
        units = []
        for seq, u in group.units.items():
            units.append([seq, u.unit_id, u.status, u.deadline, u.snapshot_seq, u.started_at, u.ended_at, u.fence,
                          u.fail_reason, u.payload is not None])
        out.append({'group_id': group_id, 'next_seq': group.next_seq, 'watermark': group.watermark,
                    'admit_seq': group.admit_seq, 'units': units, 'unit_ids': [[k, v] for k, v in group.unit_ids.items()],
                    'enrich': [[k, v] for k, v in group.enrich.items()]})
    return out


# --- fakes ---------------------------------------------------------------------------------------------------------


async def enter(kind: str, record: dict) -> None:
    """A counted fake call: an attempt, a yield, the record, the store clock move, and the cancel at call k."""
    case = state['case']
    state['attempts'] += 1
    state['in_flight'] += 1
    state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
    try:
        await asyncio.sleep(0)
        events.append({'kind': kind, **record})
        state['tasks'].append(task_label())
        state['count'] += 1
        for item in case.get('store_advance', []):
            if kind == 'store' and item[0] == record['method'] and item[1] > 0:
                item[1] -= 1
                state['clock'].now += item[2]
        if state['count'] - 1 == state['cancel_at']:
            state['calls'][case['main']].cancel()
    finally:
        state['in_flight'] -= 1


def task_label() -> str:
    current = asyncio.current_task()
    for key, task in state['calls'].items():
        if task is current:
            return key
    return '<driver>' if current is state['driver'] else '<inner>'


def make_store(mods, inner):
    def logged(name):
        async def call(self, *args):
            bound = inspect.signature(getattr(inner, name)).bind(*args)
            bound.apply_defaults()
            shown = [a for a in bound.arguments.values() if not isinstance(a, bytes)]
            await enter('store', {'method': name, 'args': encode(shown)})
            number = state['store_calls'][name] = state['store_calls'].get(name, 0) + 1
            if number in state['case'].get('store_hold', {}).get(name, []):
                try:
                    await asyncio.sleep(HOLD_SECONDS)
                except asyncio.CancelledError:
                    await asyncio.sleep(state['case']['hold_cleanup'])
                    events.append({'kind': 'store_cleanup', 'method': name})
                    raise
            if number in state['case'].get('store_fail', {}).get(name, []):
                raise RuntimeError(f'store {name} failed')
            return await getattr(inner, name)(*args)

        return call

    Store = type('RecordingStore', (mods['IngestionStateStore'],), {
        name: logged(name) for name in ('begin_admit', 'end_admit', 'start_analysis', 'save_change_set', 'heartbeat',
                                        'begin_commit', 'load_delta', 'save_final', 'finish_commit', 'fail',
                                        'ready_enrich', 'finish_enrich', 'close')})
    return Store()


class RecordingSpan:
    def __init__(self, name: str):
        self.name = name
        self.attributes: dict = {}
        self.status = None

    def add_attributes(self, attributes: dict) -> None:
        self.attributes.update(attributes)

    def set_status(self, status: str, description=None) -> None:
        self.status = status

    def record_exception(self, exception) -> None:
        self.status = 'error'


class RecordingTracer:
    def __init__(self):
        self.spans: list[RecordingSpan] = []
        self.in_flight = 0
        self.max_in_flight = 0

    def start_span(self, name: str):
        tracer = self

        class Scope:
            def __enter__(self):
                span = RecordingSpan(name)
                tracer.spans.append(span)
                tracer.in_flight += 1
                tracer.max_in_flight = max(tracer.max_in_flight, tracer.in_flight)
                return span

            def __exit__(self, *exc):
                tracer.in_flight -= 1
                return False

        return Scope()


def make_error(kind: str, unit, mods):
    if kind == 'runtime':
        return RuntimeError('analysis failed')
    if kind == 'value':
        return ValueError('bad value')
    if kind == 'expired':
        return mods['UnitExpiredError'](unit.group_id, unit.seq)
    if kind == 'stale':
        return mods['StaleCommitError'](unit.group_id, unit.seq)
    raise AssertionError(kind)


# --- views ---------------------------------------------------------------------------------------------------------


def call_view(task) -> list:
    if not task.done():
        return ['pending']
    if task.cancelled():
        return ['CancelledError', '']
    error = task.exception()
    if error is None:
        return ['ok', encode(task.result())]
    return [type(error).__name__, str(error)]


def groups_view(ingestor) -> list:
    return [[group_id, g.window.locked(), g.admit_lock.locked(), g.changed.is_set()]
            for group_id, g in ingestor._groups.items()]


def spans_view(tracer) -> list:
    return [[s.name, encode(s.attributes), s.status] for s in tracer.spans]


def snapshot(env) -> dict:
    return {'kind': 'snapshot', 'calls': [[k, call_view(t)] for k, t in state['calls'].items()],
            'groups': groups_view(env.ingestor), 'spans': spans_view(env.tracer)}


# --- cases ---------------------------------------------------------------------------------------------------------


def cases() -> list[dict]:
    out: list[dict] = []

    def case(name, actions, config=None, setup=(), **extra):
        out.append({'name': name, 'config': {'window': 2, 'lease_seconds': 0.75, 'write_margin_seconds': 0.0,
                                             'poll_interval_seconds': 0.25, **(config or {})},
                    'setup': [list(s) for s in setup], 'actions': [list(a) for a in actions], **extra})

    admit1 = [('admit', 'u1')]
    admit2 = [('admit', 'u1'), ('admit', 'u2')]

    # __init__, _group, notify, close.
    case('init_with_no_store', [('init',), ('group', 'g'), ('group', 'g'), ('close',)], config={'window': 1},
         no_store=True)
    case('init_with_defaults', [('init',), ('group', 'g'), ('close',)], defaults=True)
    case('init_with_a_store', [('init',), ('group', 'g'), ('group', 'h'), ('group', 'g'), ('snapshot',), ('close',)],
         config={'window': 3})
    case('notify', [('group', 'g'), ('notify_view', 'g'), ('notify_view', 'g')])

    # The wait function of _wait_local.
    case('wait_poll_interval', [('wait', 'w', 'g'), ('drain',), ('loop', 0.125), ('drain',), ('snapshot',),
                                ('loop', 0.1875), ('drain',), ('snapshot',)])
    case('wait_poll_on_the_clock', [('wait', 'w', 'g'), ('drain',), ('loop', 0.25), ('drain',), ('snapshot',)])
    case('wait_notify', [('wait', 'w', 'g'), ('wait', 'x', 'g'), ('drain',), ('snapshot',), ('notify', 'g'),
                         ('drain',), ('snapshot',), ('wait', 'y', 'g'), ('drain',), ('snapshot',), ('loop', 0.375),
                         ('drain',), ('snapshot',)])
    case('wait_smallest_poll_interval', [('wait', 'w', 'g'), ('drain',), ('snapshot',)],
         config={'poll_interval_seconds': 5e-324})
    case('wait_reads_the_event_at_the_call', [('wait_fn', 'f', 'g'), ('notify', 'g'), ('call_wait', 'w', 'f'),
                                              ('drain',), ('snapshot',), ('notify', 'g'), ('drain',), ('snapshot',)])
    case('wait_cancel', [('wait', 'w', 'g'), ('drain',), ('cancel', 'w'), ('drain',), ('snapshot',)])
    case('wait_other_group', [('wait', 'w', 'g'), ('drain',), ('notify', 'h'), ('drain',), ('snapshot',),
                              ('loop', 0.375), ('drain',), ('snapshot',)])

    # _heartbeat.
    case('heartbeat_renews', [('heartbeat', 'h', 1, None), ('drain',), ('loop', 0.125), ('drain',), ('snapshot',),
                              ('loop', 0.1875), ('drain',), ('store',), ('clock', 0.5), ('loop', 0.25), ('drain',),
                              ('store',), ('cancel', 'h'), ('drain',), ('snapshot',), ('loop', 0.75), ('drain',)],
         setup=admit1)
    case('heartbeat_default_lease', [('heartbeat', 'h', 1, None), ('drain',), ('loop', 19.9375), ('drain',),
                                     ('snapshot',), ('loop', 0.0625), ('drain',), ('store',), ('cancel', 'h'),
                                     ('drain',), ('snapshot',)],
         config={'lease_seconds': 60.0, 'write_margin_seconds': 5.0}, setup=admit1)
    case('heartbeat_store_error', [('heartbeat', 'h', 1, None), ('drain',), ('loop', 0.3125), ('drain',),
                                   ('snapshot',), ('loop', 0.25), ('drain',), ('store',), ('cancel', 'h'), ('drain',),
                                   ('snapshot',)],
         setup=admit1, store_fail={'heartbeat': [1]})
    for name, seconds in (('heartbeat_before_the_deadline', 0.6875), ('heartbeat_on_the_deadline', 0.75),
                          ('heartbeat_after_the_deadline', 0.8125)):
        case(name, [('heartbeat', 'h', 1, None), ('drain',), ('clock', seconds), ('loop', 0.3125), ('drain',),
                    ('snapshot',), ('store',), ('loop', 0.25), ('drain',), ('snapshot',), ('cancel', 'h'), ('drain',)],
             setup=admit1)
    case('heartbeat_failed_unit', [('heartbeat', 'h', 1, None), ('drain',), ('loop', 0.3125), ('drain',),
                                   ('snapshot',), ('loop', 0.25), ('drain',), ('snapshot',)],
         setup=admit1 + [('fail', 1, 'expired')])
    case('heartbeat_stale_fence', [('heartbeat', 'h', 1, 2), ('drain',), ('loop', 0.3125), ('drain',), ('snapshot',),
                                   ('loop', 0.25), ('drain',), ('snapshot',)],
         setup=admit1 + [('begin_commit', 1)])
    case('heartbeat_turn_of_a_different_attempt', [('heartbeat', 'h', 1, None), ('drain',), ('loop', 0.3125),
                                                   ('drain',), ('snapshot',), ('loop', 0.25), ('drain',),
                                                   ('snapshot',), ('store',), ('cancel', 'h'), ('drain',)],
         setup=admit1 + [('begin_commit', 1)])
    # The sleep of the heartbeat with a lease of inf (Python sleeps for ever) and of 1e9 (a delay above the limit of
    # Task.Delay). The case with inf admits no unit, because JSON has no inf for its deadline.
    for name, seconds in (('heartbeat_lease_of_inf', 'inf'), ('heartbeat_lease_of_1e9', 1e9)):
        case(name, [('heartbeat', 'h', 1, None), ('drain',), ('loop', 1000000.0), ('drain',), ('snapshot',),
                    ('cancel', 'h'), ('drain',), ('snapshot',)],
             config={'lease_seconds': seconds}, setup=() if seconds == 'inf' else admit1)
    # A cancel of the heartbeat task while its store call waits: the task ends canceled and logs nothing.
    case('heartbeat_cancel_during_the_store_call',
         [('heartbeat', 'h', 1, None), ('drain',), ('loop', 0.25), ('drain',), ('snapshot',), ('cancel', 'h'),
          ('drain',), ('snapshot',), ('loop', 0.0625), ('drain',), ('snapshot',)],
         setup=admit1, store_hold={'heartbeat': [1]}, hold_cleanup=0.0625)
    case('heartbeat_with_the_turn', [('heartbeat', 'h', 1, 1), ('drain',), ('clock', 0.5), ('loop', 0.3125),
                                     ('drain',), ('store',), ('cancel', 'h'), ('drain',), ('snapshot',)],
         setup=admit1 + [('begin_commit', 1)])

    # _run_unit up to the end of the Analyze phase.
    def run(name, analyze, setup=admit1, actions=(), seq=1, unit_id='u1', **extra):
        case(name, [('group', 'g'), ('run_unit', 'r', seq, unit_id, analyze), *actions, ('drain',), ('snapshot',),
                    ('store',), ('loop', 0.75), ('drain',), ('snapshot',)],
             setup=setup, main='r', **extra)

    run('analysis_fails', [['call'], ['raise', 'runtime']], cancel=True)
    run('analysis_raises_a_value_error', [['call'], ['call'], ['raise', 'value']], cancel=True)
    run('analysis_raises_unit_expired', [['call'], ['raise', 'expired']])
    run('analysis_raises_stale_commit', [['call'], ['raise', 'stale']])
    run('analysis_fails_after_heartbeats', [['call'], ['sleep', 0.6875], ['call'], ['raise', 'runtime']],
        actions=[('drain',), ('loop', 0.3125), ('drain',), ('snapshot',), ('loop', 0.3125), ('drain',),
                 ('loop', 0.125), ('drain',)],
        store_advance=[['heartbeat', 3, 0.25]], cancel=True)
    run('analysis_marks_the_unit_failed', [['call'], ['mark_failed'], ['raise', 'runtime']], cancel=True)
    run('fail_unit_store_error', [['call'], ['raise', 'runtime']], store_fail={'fail': [1]})
    run('first_heartbeat_store_error', [['call'], ['raise', 'runtime']], store_fail={'heartbeat': [1]}, cancel=True)
    run('unit_expired_before_the_analysis', [['call'], ['raise', 'runtime']],
        setup=admit1 + [('advance', 0.8125)])
    run('unit_deadline_on_the_clock', [['call'], ['raise', 'runtime']], setup=admit1 + [('advance', 0.75)])
    run('unit_deadline_before_the_clock', [['call'], ['raise', 'runtime']], setup=admit1 + [('advance', 0.6875)])
    run('unit_failed_before_the_analysis', [['call'], ['raise', 'runtime']], setup=admit1 + [('fail', 1, 'expired')])
    run('turn_of_a_different_attempt', [['call'], ['raise', 'runtime']], setup=admit1 + [('begin_commit', 1)],
        cancel=True)
    run('heartbeat_error_during_the_analysis', [['call'], ['gate'], ['raise', 'runtime']],
        actions=[('drain',), ('loop', 0.3125), ('drain',), ('loop', 0.25), ('drain',), ('snapshot',),
                 ('release', 'r'), ('drain',)],
        store_fail={'heartbeat': [2]}, cancel=True)
    run('unit_expires_during_the_analysis', [['call'], ['gate'], ['raise', 'runtime']],
        actions=[('drain',), ('clock', 1.0), ('loop', 0.3125), ('drain',), ('snapshot',), ('loop', 0.25),
                 ('drain',), ('release', 'r'), ('drain',)], cancel=True)
    run('cancel_during_the_analysis', [['call'], ['gate'], ['raise', 'runtime']],
        actions=[('drain',), ('snapshot',), ('cancel', 'r'), ('drain',)])
    run('cancel_before_the_call_starts', [['call'], ['raise', 'runtime']], actions=[('cancel', 'r'), ('drain',)])
    # The cleanup of _run_unit: the heartbeat task is in a store call that takes loop time after a cancel. The cleanup
    # of that call ends, then _run_unit ends, then the waiter of the group wakes.
    case('cleanup_waits_for_the_heartbeat_task',
         [('group', 'g'), ('run_unit', 'r', 1, 'u1', [['call'], ['gate'], ['raise', 'runtime']]), ('drain',),
          ('loop', 0.25), ('drain',), ('wait', 'w', 'g'), ('drain',), ('snapshot',), ('release', 'r'), ('drain',),
          ('snapshot',), ('loop', 0.03125), ('drain',), ('snapshot',), ('loop', 0.03125), ('drain',), ('snapshot',),
          ('store',)],
         config={'poll_interval_seconds': 50.0}, setup=admit1, main='r', store_hold={'heartbeat': [2]},
         hold_cleanup=0.0625)
    case('two_units_and_a_waiter',
         [('group', 'g'), ('run_unit', 'r1', 1, 'u1', [['call'], ['gate'], ['raise', 'runtime']]),
          ('run_unit', 'r2', 2, 'u2', [['call'], ['gate'], ['raise', 'value']]), ('wait', 'w', 'g'), ('drain',),
          ('snapshot',), ('release', 'r1'), ('drain',), ('snapshot',), ('wait', 'x', 'g'), ('drain',),
          ('release', 'r2'), ('drain',), ('snapshot',), ('store',)],
         setup=admit2)

    # add_episode and add_episode_bulk up to the admit lock.
    def bulk_calls(counts):
        return [('add_bulk', f'b{i}', 'g', n) for i, n in enumerate(counts)]

    case('window_more_calls_than_the_window',
         [('group', 'g'), ('hold_admit', 'g'), *bulk_calls([1, 2, 3, 4, 5]), ('drain',), ('snapshot',),
          ('cancel', 'b0'), ('drain',), ('snapshot',), ('cancel', 'b3'), ('drain',), ('snapshot',),
          ('cancel', 'b1'), ('drain',), ('snapshot',)])
    case('window_on_its_limit', [('group', 'g'), ('hold_admit', 'g'), *bulk_calls([0, 1]), ('drain',), ('snapshot',)])
    case('window_of_one',
         [('group', 'g'), ('hold_admit', 'g'), ('add', 'a0', 'g', False), ('add', 'a1', 'g', False),
          ('add', 'a2', 'g', True), ('drain',), ('snapshot',), ('cancel', 'a0'), ('drain',), ('snapshot',),
          ('cancel', 'a1'), ('drain',), ('snapshot',)],
         config={'window': 1})
    case('window_of_one_with_one_call',
         [('group', 'g'), ('hold_admit', 'g'), ('add', 'a0', 'g', True), ('drain',), ('snapshot',)],
         config={'window': 1})
    case('cancel_a_call_that_waits_for_the_window',
         [('group', 'g'), ('hold_window', 'g'), ('add', 'a0', 'g', True), ('drain',), ('snapshot',), ('cancel', 'a0'),
          ('drain',), ('snapshot',), ('release_window', 'g'), ('snapshot',)],
         config={'window': 1})
    case('cancel_a_call_that_waits_for_the_admit_lock',
         [('group', 'g'), ('hold_admit', 'g'), ('add', 'a0', 'g', False), ('add_bulk', 'b0', 'g', 2), ('drain',),
          ('snapshot',), ('cancel', 'b0'), ('drain',), ('cancel', 'a0'), ('drain',), ('snapshot',)])
    case('two_graphs',
         [('group', 'g'), ('group', 'h'), ('hold_admit', 'g'), ('hold_admit', 'h'), *bulk_calls([1, 2, 3]),
          ('add_bulk', 'c0', 'h', 4), ('add_bulk', 'c1', 'h', 5), ('drain',), ('snapshot',)],
         config={'window': 1})
    return out


async def run_case(case: dict, mods) -> dict:
    state['driver'] = asyncio.current_task()
    # JSON has no inf, so a case writes the text 'inf'.
    case = {**case, 'config': {k: float(v) if v == 'inf' else v for k, v in case['config'].items()}}
    clock = FakeClock()
    state['clock'] = clock
    inner = mods['InMemoryIngestionStateStore'](clock=clock)
    store = make_store(mods, inner)
    for step in case['setup']:
        op = step[0]
        if op == 'admit':
            admission = await inner.begin_admit('g', step[1], case['config']['lease_seconds'])
            await inner.end_admit('g', admission.seq)
        elif op == 'advance':
            clock.now += step[1]
        elif op == 'fail':
            await inner.fail('g', step[1], step[2])
        elif op == 'begin_commit':
            await inner.begin_commit('g', step[1], case['config']['lease_seconds'])
        else:
            raise AssertionError(op)

    def skeleton(*args, **kwargs):
        raise AssertionError('a skeleton member of GraphitiCore: this path waits for layer 9')

    tracer = RecordingTracer()
    graphiti = SimpleNamespace(tracer=tracer, _resolve_request_scope=skeleton)
    Config = mods['ConcurrentIngestionConfig']
    if case.get('defaults'):
        config = Config(window=case['config']['window'], state_store=store)
    elif case.get('no_store'):
        config = Config(**case['config'])
    else:
        config = Config(state_store=store, **case['config'])
    ingestor = mods['ConcurrentIngestor'](graphiti, config)
    env = SimpleNamespace(ingestor=ingestor, tracer=tracer, inner=inner)
    loop = asyncio.get_running_loop()
    calls: dict = state['calls']
    gates: dict = {}
    waits: dict = {}
    seq_of: dict = {}

    async def analyze_fake(unit, spec):
        for index, step in enumerate(spec):
            op = step[0]
            if op == 'call':
                await enter('analyze', {'seq': unit.seq, 'step': index})
            elif op == 'gate':
                await gates[unit.seq].wait()
            elif op == 'sleep':
                await asyncio.sleep(step[1])
            elif op == 'mark_failed':
                await ingestor.store.fail(unit.group_id, unit.seq, 'analysis')
            elif op == 'raise':
                raise make_error(step[1], unit, mods)
        raise AssertionError('an analysis that returns calls commit_unit, which waits for layer 9')

    def raw_episodes(count):
        return [mods['RawEpisode'](name=f'episode-{i}', content='content', source_description='test',
                                   source=mods['EpisodeType'].text, reference_time=T0) for i in range(count)]

    for action in case['actions']:
        op = action[0]
        if op == 'drain':
            for _ in range(DRAIN):
                await asyncio.sleep(0)
        elif op == 'loop':
            # Two timers on one time fire in an order that the port does not promise (GAPS G19), so no case has them.
            due = [h.when() for h in loop._scheduled if not h.cancelled() and h.when() <= loop.now + action[1]]
            if len(due) != len(set(due)):
                raise SystemExit(f'{case["name"]}: two timers fire at one time: {sorted(due)}')
            loop.now += action[1]
        elif op == 'clock':
            clock.now += action[1]
        elif op == 'snapshot':
            events.append(snapshot(env))
        elif op == 'store':
            events.append({'kind': 'store_state', 'store': store_view(inner)})
        elif op == 'init':
            events.append({'kind': 'init', 'same_graphiti': ingestor.graphiti is graphiti,
                           'same_config': ingestor.config is config,
                           'store': 'given' if ingestor.store is store else type(ingestor.store).__name__,
                           'config_store': ingestor.store is config.state_store, 'groups': list(ingestor._groups)})
        elif op == 'group':
            before = list(ingestor._groups)
            first = ingestor._groups.get(action[1])
            group = ingestor._group(action[1])
            events.append({'kind': 'group', 'group_id': action[1], 'before': before, 'same': group is first,
                           'window_locked': group.window.locked(), 'admit_locked': group.admit_lock.locked(),
                           'changed': group.changed.is_set()})
        elif op == 'notify_view':
            group = ingestor._group(action[1])
            old = group.changed
            group.notify()
            events.append({'kind': 'notify', 'old_set': old.is_set(), 'new_set': group.changed.is_set(),
                           'replaced': group.changed is not old})
        elif op == 'notify':
            ingestor._group(action[1]).notify()
        elif op == 'wait':
            calls[action[1]] = asyncio.ensure_future(ingestor._wait_local(ingestor._group(action[2]))())
        elif op == 'wait_fn':
            waits[action[1]] = ingestor._wait_local(ingestor._group(action[2]))
        elif op == 'call_wait':
            calls[action[1]] = asyncio.ensure_future(waits[action[2]]())
        elif op == 'heartbeat':
            lease = mods['UnitLease'](ingestor.store, config, 'g', action[2])
            lease.fence = action[3]
            calls[action[1]] = asyncio.ensure_future(ingestor._heartbeat(lease))
        elif op == 'run_unit':
            seq, unit_id, spec = action[2], action[3], action[4]
            gates[seq] = asyncio.Event()
            seq_of[action[1]] = seq
            unit = mods['AdmittedUnit'](group_id='g', unit_id=unit_id, seq=seq, created_at=T0, episodes=[],
                                        previous_episodes=[])
            span = RecordingSpan('add_episode')
            tracer.spans.append(span)

            async def analyze(u, spec=spec):
                return await analyze_fake(u, spec)

            calls[action[1]] = asyncio.ensure_future(ingestor._run_unit(
                ingestor._group('g'), unit, span, analyze,
                {'saga': None, 'saga_previous_episode_uuid': None, 'identity_properties': None}, None))
        elif op == 'release':
            gates[seq_of[action[1]]].set()
        elif op == 'cancel':
            calls[action[1]].cancel()
        elif op == 'hold_admit':
            await ingestor._group(action[1]).admit_lock.acquire()
        elif op == 'hold_window':
            await ingestor._group(action[1]).window.acquire()
        elif op == 'release_window':
            ingestor._group(action[1]).window.release()
        elif op == 'add':
            kwargs = dict(name=f'episode-{action[1]}', episode_body='content', source_description='test',
                          reference_time=T0, source=mods['EpisodeType'].text, group_id=action[2], uuid=None,
                          entity_types=None, excluded_entity_types=None, previous_episode_uuids=None, edge_types=None,
                          edge_type_map=None, custom_extraction_instructions=None, saga=None,
                          saga_previous_episode_uuid=None)
            if not action[3]:
                kwargs['identity_properties'] = {'Person': ['email']}
            calls[action[1]] = asyncio.ensure_future(ingestor.add_episode(**kwargs))
        elif op == 'add_bulk':
            calls[action[1]] = asyncio.ensure_future(ingestor.add_episode_bulk(
                bulk_episodes=raw_episodes(action[3]), group_id=action[2], entity_types=None,
                excluded_entity_types=None, edge_types=None, edge_type_map=None, strict_edge_types=False,
                custom_extraction_instructions=None, saga=None, use_combined_extraction=True))
        elif op == 'close':
            await ingestor.close()
            events.append({'kind': 'closed'})
        else:
            raise AssertionError(op)

    # The end of each case: cancel each call that has not ended, and release each admit lock of the driver.
    pending = [k for k, t in calls.items() if not t.done()]
    for key in pending:
        calls[key].cancel()
    for _ in range(DRAIN):
        await asyncio.sleep(0)
    for group in ingestor._groups.values():
        if group.admit_lock.locked():
            group.admit_lock.release()
    leftover = [t for t in asyncio.all_tasks() if t is not asyncio.current_task()]
    if leftover:
        raise SystemExit(f'{case["name"]}: tasks stay after the case: {leftover}')
    return {'pending_at_end': pending, 'calls': [[k, call_view(t)] for k, t in calls.items()],
            'groups': groups_view(ingestor), 'spans': spans_view(tracer), 'max_spans': tracer.max_in_flight,
            'store': store_view(inner)}


def run_one(case: dict, mods, cancel_at: int) -> dict:
    events.clear()
    state.update(in_flight=0, max_in_flight=0, count=0, attempts=0, cancel_at=cancel_at, case=copy.deepcopy(case),
                 calls={}, store_calls={}, tasks=[], driver=None)
    with asyncio.Runner(loop_factory=VirtualLoop) as runner:
        output = runner.run(run_case(state['case'], mods))
    output['events'] = list(events)
    output['max_in_flight'] = state['max_in_flight']
    output['count'] = state['count']
    output['attempts'] = state['attempts']
    output['tasks'] = list(state['tasks'])
    return output


def record(mods) -> list[dict]:
    out = []
    for case in cases():
        output = run_one(case, mods, -1)
        if case.get('cancel'):
            cancels = []
            for k in range(output['count']):
                cancelled = run_one(case, mods, k)
                kept = [e for e in cancelled['events'] if e['kind'] in ('store', 'analyze', 'log')]
                at = [i for i, e in enumerate(kept) if e['kind'] != 'log'][k]
                main = dict(cancelled['calls'])[case['main']]
                cancels.append({'k': k, 'attempts': cancelled['attempts'], 'count': cancelled['count'],
                                'error': main[0], 'after': kept[at + 1:]})
            output['cancel'] = cancels
        out.append({'in': case, 'out': output})
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(UPSTREAM))

    from graphiti_core.errors import StaleCommitError, UnitExpiredError
    from graphiti_core.ingestion.config import ConcurrentIngestionConfig
    from graphiti_core.ingestion.phases import AdmittedUnit, UnitLease
    from graphiti_core.ingestion.scheduler import ConcurrentIngestor
    from graphiti_core.ingestion.state_store import IngestionStateStore, InMemoryIngestionStateStore
    from graphiti_core.nodes import EpisodeType
    from graphiti_core.utils.bulk_utils import RawEpisode

    class Capture(logging.Handler):
        def emit(self, record):
            entry = {'kind': 'log', 'level': record.levelname, 'message': record.getMessage()}
            if record.exc_info:
                entry['exc'] = True
            events.append(entry)

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    mods = dict(ConcurrentIngestor=ConcurrentIngestor, ConcurrentIngestionConfig=ConcurrentIngestionConfig,
                AdmittedUnit=AdmittedUnit, UnitLease=UnitLease, IngestionStateStore=IngestionStateStore,
                InMemoryIngestionStateStore=InMemoryIngestionStateStore, EpisodeType=EpisodeType,
                RawEpisode=RawEpisode, UnitExpiredError=UnitExpiredError, StaleCommitError=StaleCommitError)
    records = record(mods)
    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = ('{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","cases":[\n'
            + ',\n'.join(dump(r) for r in records) + '\n]}\n')
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases, {size} bytes')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
