"""Records what real asyncio does for small scripted programs, for the Asyncio helpers of PyCompat.

    .upstream/graphiti/.venv/bin/python port/tools/generate_asyncio_values.py
    .upstream/graphiti/.venv/bin/python port/tools/generate_asyncio_values.py --check

The output is tests/Graphiti.Net.Property.Tests/python-values.asyncio.json. --check writes nothing and exits 1 if
the file differs from what the run would write. The tool needs Python 3.12, the version of the pinned upstream, and
it imports `semaphore_gather` from the upstream package, so it runs in the upstream virtual environment.

Timing is not an oracle, but order is. Each script is a set of named coroutines and a `main`. A coroutine is a list
of operations: it writes log entries, yields to the loop with `await asyncio.sleep(0)`, raises, returns, and calls
gather, semaphore_gather, create_task, cancel, shield, wait_for, Event, and to_thread. The tool runs each script on
asyncio and records the trace: the log entries in the order in which they happened, up to the end of `main`.
The C# tests run the same scripts on the Asyncio helpers and compare the traces.

Operations (the C# interpreter in AsyncioScripts.cs reads the same list):

    ["log", text]                       append "<coroutine>:<text>"
    ["yield"]                           await asyncio.sleep(0)
    ["raise", cls, message]             raise ValueError, RuntimeError, or TimeoutError
    ["return", value]                   return an int (no return gives None)
    ["call", name]                      await name() in the same task
    ["try", ops, mode, handler?]        mode "swallow": log and swallow an Exception, log and re-raise
                                        CancelledError. "reraise": log and re-raise any. "all": log and swallow any.
                                        The optional handler ops run after the log, before a re-raise.
    ["gather", [names], rx]             await asyncio.gather(*coroutines, return_exceptions=rx)
    ["sgather", [names], limit]         await semaphore_gather(*coroutines, max_coroutines=limit)
    ["create_task", name, handle]       asyncio.create_task(name())
    ["cancel", handle]                  task.cancel()
    ["await", handle]                   await task
    ["shield", handle]                  await asyncio.shield(task)
    ["gather_tasks", [handles], rx]     await asyncio.gather(*tasks, return_exceptions=rx)
    ["wait_for", name, timeout]         await asyncio.wait_for(name(), timeout)
    ["ev_wait", e], ["ev_set", e], ["ev_clear", e], ["ev_is_set", e]
    ["to_thread", value]                await asyncio.to_thread(lambda: value)
    ["sleep", text]                     await asyncio.sleep(float(text)), then log "slept <text>". The text is a float
                                        literal that both sides read, also "Infinity", "-Infinity", and "NaN".

Part of the scripts come from a fixed seed. A random script that does not end in Python within half a second is
dropped (it waits for an event that no coroutine sets). Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import random
import sys
import warnings
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VALUES_PATH = ROOT / 'tests' / 'Graphiti.Net.Property.Tests' / 'python-values.asyncio.json'

if sys.version_info[:2] != (3, 12):
    sys.exit(f'generate_asyncio_values: needs Python 3.12, found {sys.version.split()[0]}')

from graphiti_core import helpers  # noqa: E402  (after the version test)

# A canceled semaphore_gather leaves coroutine objects that never ran, and a gather that failed leaves tasks whose
# exception nobody reads. Python warns about both. The warnings are expected here and say nothing about the trace.
warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)
logging.getLogger('asyncio').setLevel(logging.CRITICAL)

SEED = 20261126
RANDOM_SCRIPTS = 80
ERRORS = {'ValueError': ValueError, 'RuntimeError': RuntimeError, 'TimeoutError': TimeoutError}


def fmt(v) -> str:
    """The text of a value in a trace. The C# interpreter writes the same text."""
    if v is None:
        return 'None'
    if isinstance(v, bool):
        return 'True' if v else 'False'
    if isinstance(v, int):
        return str(v)
    if isinstance(v, list):
        return '[' + ', '.join(fmt(x) for x in v) + ']'
    if isinstance(v, asyncio.CancelledError):
        return 'CancelledError'
    if isinstance(v, BaseException):
        return f'{type(v).__name__}({v})'
    raise TypeError(f'no trace text for {v!r}')


class Program:
    def __init__(self, script: dict):
        self.coros = script['coros']
        self.main = script['main']
        self.trace: list[str] = []
        self.tasks: dict[str, asyncio.Task] = {}
        self.events: dict[str, asyncio.Event] = {}

    def log(self, me: str, text: str) -> None:
        self.trace.append(f'{me}:{text}')

    def event(self, name: str) -> asyncio.Event:
        if name not in self.events:
            self.events[name] = asyncio.Event()
        return self.events[name]

    async def run(self, name: str):
        _, value = await self.block(name, self.main if name == 'main' else self.coros[name])
        return value

    async def tracked(self, state: list[int], name: str):
        state[0] += 1
        state[1] = max(state[1], state[0])
        try:
            return await self.run(name)
        finally:
            state[0] -= 1

    async def block(self, me: str, ops: list) -> tuple[bool, object]:
        for op in ops:
            kind = op[0]
            if kind == 'log':
                self.log(me, op[1])
            elif kind == 'yield':
                await asyncio.sleep(0)
            elif kind == 'raise':
                raise ERRORS[op[1]](op[2])
            elif kind == 'return':
                return True, op[1]
            elif kind == 'call':
                r = await self.run(op[1])
                self.log(me, f'call {op[1]} -> {fmt(r)}')
            elif kind == 'try':
                mode = op[2]
                try:
                    returned, value = await self.block(me, op[1])
                    if returned:
                        return True, value
                except asyncio.CancelledError as e:
                    self.log(me, f'caught {fmt(e)}')
                    await self.block(me, op[3] if len(op) > 3 else [])
                    if mode != 'all':
                        raise
                except Exception as e:
                    self.log(me, f'caught {fmt(e)}')
                    await self.block(me, op[3] if len(op) > 3 else [])
                    if mode == 'reraise':
                        raise
            elif kind == 'gather':
                r = await asyncio.gather(*(self.run(n) for n in op[1]), return_exceptions=op[2])
                self.log(me, f'gather -> {fmt(r)}')
            elif kind == 'sgather':
                state = [0, 0]
                r = await helpers.semaphore_gather(*(self.tracked(state, n) for n in op[1]), max_coroutines=op[2])
                self.log(me, f'sgather -> {fmt(r)} max={state[1]}')
            elif kind == 'create_task':
                self.tasks[op[2]] = asyncio.create_task(self.run(op[1]))
            elif kind == 'cancel':
                self.tasks[op[1]].cancel()
            elif kind == 'await':
                r = await self.tasks[op[1]]
                self.log(me, f'await {op[1]} -> {fmt(r)}')
            elif kind == 'shield':
                r = await asyncio.shield(self.tasks[op[1]])
                self.log(me, f'shield {op[1]} -> {fmt(r)}')
            elif kind == 'gather_tasks':
                r = await asyncio.gather(*(self.tasks[h] for h in op[1]), return_exceptions=op[2])
                self.log(me, f'gather_tasks -> {fmt(r)}')
            elif kind == 'wait_for':
                r = await asyncio.wait_for(self.run(op[1]), op[2])
                self.log(me, f'wait_for {op[1]} -> {fmt(r)}')
            elif kind == 'ev_wait':
                r = await self.event(op[1]).wait()
                self.log(me, f'ev_wait {op[1]} -> {fmt(r)}')
            elif kind == 'ev_set':
                self.event(op[1]).set()
            elif kind == 'ev_clear':
                self.event(op[1]).clear()
            elif kind == 'ev_is_set':
                self.log(me, f'ev_is_set {op[1]} -> {fmt(self.event(op[1]).is_set())}')
            elif kind == 'sleep':
                await asyncio.sleep(float(op[1]))
                self.log(me, f'slept {op[1]}')
            elif kind == 'to_thread':
                value = op[1]
                r = await asyncio.to_thread(lambda: value)
                self.log(me, f'to_thread -> {fmt(r)}')
            else:
                raise ValueError(f'unknown operation {kind}')
        return False, None


def run_script(script: dict, guard: float | None = None) -> list[str] | None:
    """The trace of `main`, or None if a guard is set and `main` does not end within it."""
    program = Program(script)

    async def runner():
        try:
            if guard is None:
                r = await program.run('main')
            else:
                r = await asyncio.wait_for(program.run('main'), guard)
            program.log('main', f'-> {fmt(r)}')
        except TimeoutError:
            if guard is not None:
                return None
            raise
        except BaseException as e:  # noqa: BLE001  (the trace records any outcome of main)
            program.log('main', f'raised {fmt(e)}')
        return list(program.trace)

    return asyncio.run(runner())


def s(name: str, coros: dict, main: list, note: str) -> dict:
    return {'id': name, 'note': note, 'coros': coros, 'main': main}


def steps(name: str, n: int) -> list:
    """A coroutine body that logs, yields n times with a log after each, and logs its end."""
    ops: list = [['log', 'start']]
    for i in range(n):
        ops += [['yield'], ['log', f'y{i}']]
    return ops


def hand_scripts() -> list[dict]:
    """Scripts for each behavior that the task names. Each note says what the script shows."""
    out = []
    bg = steps('bg', 6) + [['log', 'end']]
    out.append(s('gather_result_order', {
        'a': [['log', 's'], ['yield'], ['yield'], ['log', 'e'], ['return', 1]],
        'b': [['log', 's'], ['return', 2]],
        'c': [['log', 's'], ['yield'], ['log', 'e'], ['return', 3]],
    }, [['log', 'before'], ['gather', ['a', 'b', 'c'], False], ['log', 'after']],
        'results in input order, start order, interleaving at yields'))
    out.append(s('gather_empty', {}, [['gather', [], False], ['log', 'after']], 'gather() is []'))
    out.append(s('gather_first_failure_in_time', {
        'a': [['yield'], ['yield'], ['yield'], ['raise', 'ValueError', 'late']],
        'b': [['yield'], ['raise', 'ValueError', 'early']],
        'c': steps('c', 5) + [['log', 'done'], ['return', 3]],
    }, [['try', [['gather', ['a', 'b', 'c'], False]], 'swallow']] + [['yield']] * 8 + [['log', 'after']],
        'the first failure in time arrives, the others keep running'))
    out.append(s('gather_return_exceptions', {
        'a': [['yield'], ['raise', 'ValueError', 'bad']],
        'b': [['return', 2]],
        'c': [['yield'], ['yield'], ['raise', 'RuntimeError', 'worse']],
    }, [['gather', ['a', 'b', 'c'], True], ['log', 'after']], 'return_exceptions=True gives values and exceptions'))
    out.append(s('gather_return_exceptions_all_ok', {
        'a': [['yield'], ['return', 1]], 'b': [['return', None]],
    }, [['gather', ['a', 'b'], True]], 'return_exceptions=True with no failure'))
    out.append(s('gather_nested', {
        'p': [['log', 's'], ['gather', ['a', 'b'], False], ['return', 10]],
        'q': steps('q', 3) + [['return', 20]],
        'a': steps('a', 2) + [['return', 1]],
        'b': steps('b', 1) + [['return', 2]],
    }, [['gather', ['p', 'q'], False]], 'a gather inside a gather'))
    out.append(s('gather_raise_without_yield', {
        'a': [['raise', 'ValueError', 'now']], 'b': [['log', 'ran'], ['return', 2]],
    }, [['try', [['gather', ['a', 'b'], False]], 'swallow'], ['yield'], ['yield'], ['log', 'after']],
        'a call that raises before its first yield'))
    out.append(s('gather_hops_against_a_task', {
        'bg': bg, 'a': [['yield'], ['log', 'x'], ['return', 1]], 'b': [['return', 2]],
    }, [['create_task', 'bg', 't'], ['gather', ['a', 'b'], False], ['log', 'after'], ['await', 't']],
        'the trips through the ready queue of a gather, seen from a task that runs beside it'))
    out.append(s('gather_failure_hops_against_a_task', {
        'bg': bg, 'a': [['yield'], ['raise', 'ValueError', 'x']], 'b': steps('b', 4),
    }, [['create_task', 'bg', 't'], ['try', [['gather', ['a', 'b'], False]], 'swallow'], ['log', 'after'],
        ['await', 't']], 'the trips of a failed gather'))
    out.append(s('gather_main_raises', {
        'a': [['raise', 'RuntimeError', 'boom']],
    }, [['gather', ['a'], False], ['log', 'never']], 'an exception that nobody catches ends main'))
    out.append(s('sgather_limit_1', {f'c{i}': steps(f'c{i}', 2) + [['return', i]] for i in range(4)},
        [['sgather', [f'c{i}' for i in range(4)], 1]], 'a limit of 1 runs the calls one after the other'))
    out.append(s('sgather_limit_2', {f'c{i}': steps(f'c{i}', i % 3 + 1) + [['return', i]] for i in range(6)},
        [['sgather', [f'c{i}' for i in range(6)], 2]], 'at most 2 in flight, waiting calls start in order'))
    out.append(s('sgather_limit_3_of_7', {f'c{i}': steps(f'c{i}', (7 - i) % 4) + [['return', i * i]] for i in range(7)},
        [['sgather', [f'c{i}' for i in range(7)], 3]], 'at most 3 in flight'))
    out.append(s('sgather_limit_none', {f'c{i}': steps(f'c{i}', 1) for i in range(3)},
        [['sgather', ['c0', 'c1', 'c2'], None]], 'None uses SEMAPHORE_LIMIT'))
    out.append(s('sgather_limit_0', {f'c{i}': steps(f'c{i}', 1) for i in range(3)},
        [['sgather', ['c0', 'c1', 'c2'], 0]], '0 uses SEMAPHORE_LIMIT, because of `or`'))
    out.append(s('sgather_limit_above_count', {f'c{i}': steps(f'c{i}', 2) for i in range(3)},
        [['sgather', ['c0', 'c1', 'c2'], 100]], 'a limit above the count'))
    out.append(s('sgather_failure_keeps_others', {
        'c0': steps('c0', 2) + [['return', 0]],
        'c1': [['yield'], ['raise', 'ValueError', 'c1 failed']],
        'c2': steps('c2', 1) + [['return', 2]],
        'c3': steps('c3', 1) + [['return', 3]],
    }, [['try', [['sgather', ['c0', 'c1', 'c2', 'c3'], 2]], 'swallow']] + [['yield']] * 10 + [['log', 'after']],
        'the first failure arrives, the other calls run, and waiting calls still start'))
    out.append(s('sgather_negative_limit', {'c0': [['log', 'ran']]},
        [['try', [['sgather', ['c0'], -1]], 'swallow'], ['log', 'after']], 'Semaphore(-1) raises ValueError'))
    out.append(s('sgather_empty', {}, [['sgather', [], 2]], 'no calls'))
    out.append(s('sgather_nested', {
        'p': [['sgather', ['a', 'b'], 1], ['return', 1]],
        'q': [['sgather', ['a', 'b'], 2], ['return', 2]],
        'a': steps('a', 2), 'b': steps('b', 1),
    }, [['sgather', ['p', 'q'], 2]], 'semaphore_gather inside semaphore_gather'))
    out.append(s('wait_for_in_time', {'a': steps('a', 2) + [['return', 5]]},
        [['wait_for', 'a', 10.0], ['log', 'after']], 'the call ends before the limit'))
    out.append(s('wait_for_none', {'a': steps('a', 2) + [['return', 5]]},
        [['wait_for', 'a', None]], 'timeout None'))
    out.append(s('wait_for_timeout', {'a': [['log', 's'], ['try', [['ev_wait', 'never']], 'reraise'], ['log', 'never']]},
        [['try', [['wait_for', 'a', 0.005]], 'swallow'], ['log', 'after']],
        'the limit cancels the call, which sees CancelledError, and wait_for raises TimeoutError'))
    out.append(s('wait_for_timeout_swallowed', {
        'a': [['log', 's'], ['try', [['ev_wait', 'never']], 'all'], ['log', 'swallowed'], ['return', 7]],
    }, [['wait_for', 'a', 0.005], ['log', 'after']], 'a call that swallows the cancellation returns its value'))
    out.append(s('wait_for_zero', {'bg': bg, 'a': [['log', 'ran'], ['return', 1]]},
        [['create_task', 'bg', 't'], ['try', [['wait_for', 'a', 0]], 'swallow'], ['log', 'after'], ['await', 't']],
        'a timeout of 0 never starts the call'))
    out.append(s('wait_for_negative', {'a': [['log', 'ran'], ['return', 1]]},
        [['try', [['wait_for', 'a', -1.5]], 'swallow'], ['log', 'after']], 'a negative timeout'))
    out.append(s('wait_for_inner_raises', {'a': [['yield'], ['raise', 'ValueError', 'inner']]},
        [['try', [['wait_for', 'a', 10.0]], 'swallow']], 'an exception of the call passes through'))
    out.append(s('wait_for_inner_timeout_error', {'a': [['raise', 'TimeoutError', 'inner']]},
        [['try', [['wait_for', 'a', 10.0]], 'swallow']], 'a TimeoutError of the call itself'))
    out.append(s('wait_for_event_poll', {
        'setter': [['yield'], ['yield'], ['ev_set', 'changed']],
        'poll': [['ev_wait', 'changed'], ['return', 1]],
    }, [['try', [['wait_for', 'poll', 0.005]], 'swallow'], ['create_task', 'setter', 's'],
        ['wait_for', 'poll', 10.0], ['await', 's']], 'the poll of ingestion/scheduler.py'))
    # asyncio.sleep: 0 or less (also -inf) yields once, a delay above the 49.7 days of Task.Delay and inf wait with no
    # error, and nan with no other timer pending ends at once.
    for text in ('0', '-1.5', '-Infinity'):
        out.append(s(f'sleep_{text}', {'bg': bg},
            [['create_task', 'bg', 't'], ['sleep', text], ['log', 'after'], ['await', 't']],
            'a delay of 0 or less yields once (__sleep0)'))
    for text in ('1e7', '1e9', '1e300', 'Infinity'):
        out.append(s(f'sleep_{text}', {'a': [['log', 's'], ['sleep', text], ['log', 'never']]},
            [['try', [['wait_for', 'a', 0.005]], 'swallow'], ['log', 'after']],
            'a long delay waits and raises nothing; the limit of wait_for ends it'))
    out.append(s('sleep_NaN', {}, [['sleep', 'NaN'], ['log', 'after']],
        'nan sets a loop timer at nan, which runs at once when no other timer is pending'))
    out.append(s('sleep_short', {'a': [['sleep', '0.001'], ['return', 1]]},
        [['wait_for', 'a', 10.0], ['log', 'after']], 'a short delay ends'))
    out.append(s('create_task_order', {'t1': steps('t1', 2) + [['return', 1]], 't2': steps('t2', 1) + [['return', 2]]},
        [['create_task', 't1', 'h1'], ['create_task', 't2', 'h2'], ['log', 'created'], ['yield'], ['log', 'yielded'],
         ['await', 'h1'], ['await', 'h2']], 'a task starts on the next trip through the loop'))
    out.append(s('create_task_cancel_before_start', {'t': [['log', 'ran']]},
        [['create_task', 't', 'h'], ['cancel', 'h'], ['try', [['await', 'h']], 'all'], ['log', 'after']],
        'a task that is canceled before its first step never runs'))
    out.append(s('create_task_cancel_running', {'t': [['try', steps('t', 6) + [['log', 'never']], 'reraise']]},
        [['create_task', 't', 'h'], ['yield'], ['yield'], ['cancel', 'h'], ['try', [['await', 'h']], 'all'],
         ['log', 'after']], 'a running task sees CancelledError at its next yield'))
    out.append(s('cancel_through_gather', {
        't': [['try', [['gather', ['a', 'b'], False]], 'reraise']],
        'a': [['try', steps('a', 6), 'reraise']],
        'b': [['try', steps('b', 6), 'reraise']],
    }, [['create_task', 't', 'h'], ['yield'], ['yield'], ['cancel', 'h'], ['try', [['await', 'h']], 'all'],
        ['log', 'after']], 'a cancellation of a task reaches each call of its gather'))
    out.append(s('cancel_through_sgather', {
        't': [['try', [['sgather', ['a', 'b', 'c'], 1]], 'reraise']],
        'a': [['try', steps('a', 6), 'reraise']],
        'b': [['log', 'b ran']],
        'c': [['log', 'c ran']],
    }, [['create_task', 't', 'h'], ['yield'], ['yield'], ['cancel', 'h'], ['try', [['await', 'h']], 'all'],
        ['log', 'after']], 'a cancellation ends the running call, and the waiting calls never start'))
    out.append(s('shield_result', {'inner': steps('inner', 2) + [['return', 4]]},
        [['create_task', 'inner', 'h'], ['shield', 'h']], 'shield gives the result of the task'))
    out.append(s('shield_outer_canceled', {
        'inner': steps('inner', 5) + [['log', 'done'], ['return', 9]],
        'u': [['try', [['shield', 'h']], 'reraise']],
    }, [['create_task', 'inner', 'h'], ['create_task', 'u', 'hu'], ['yield'], ['yield'], ['cancel', 'hu'],
        ['try', [['await', 'hu']], 'all'], ['log', 'u ended'], ['await', 'h']],
        'the awaiter of a shield gets CancelledError, and the inner task runs on'))
    out.append(s('shield_done_task', {'inner': [['return', 3]]},
        [['create_task', 'inner', 'h'], ['yield'], ['yield'], ['shield', 'h']], 'shield of a task that has ended'))
    out.append(s('shield_inner_raises', {'inner': [['yield'], ['raise', 'ValueError', 'in']]},
        [['create_task', 'inner', 'h'], ['try', [['shield', 'h']], 'swallow']], 'shield passes an exception on'))
    out.append(s('event_waiters_in_order', {
        'w1': [['ev_wait', 'e'], ['log', 'woke']], 'w2': [['ev_wait', 'e'], ['log', 'woke']],
        'w3': [['yield'], ['ev_wait', 'e'], ['log', 'woke']],
        'setter': [['yield'], ['yield'], ['log', 'set'], ['ev_set', 'e'], ['log', 'after set']],
    }, [['gather', ['w1', 'w2', 'w3', 'setter'], False], ['ev_is_set', 'e']], 'set wakes each waiter in order'))
    out.append(s('event_set_before_wait', {
        'bg': bg, 'w': [['ev_wait', 'e'], ['log', 'woke']],
    }, [['create_task', 'bg', 't'], ['ev_set', 'e'], ['call', 'w'], ['log', 'after'], ['await', 't']],
        'wait on a set event returns with no yield'))
    out.append(s('event_set_then_clear', {
        'w1': [['ev_wait', 'e'], ['log', 'woke']], 'w2': [['ev_wait', 'e'], ['log', 'woke']],
        'setter': [['yield'], ['ev_set', 'e'], ['ev_clear', 'e'], ['ev_is_set', 'e']],
    }, [['gather', ['w1', 'w2', 'setter'], False], ['ev_is_set', 'e']], 'a woken waiter stays woken after clear'))
    out.append(s('event_cancel_one_waiter', {
        'w1': [['try', [['ev_wait', 'e']], 'reraise'], ['log', 'woke']],
        'w2': [['ev_wait', 'e'], ['log', 'woke']],
    }, [['create_task', 'w1', 'h1'], ['create_task', 'w2', 'h2'], ['yield'], ['cancel', 'h1'], ['yield'],
        ['ev_set', 'e'], ['try', [['await', 'h1']], 'all'], ['await', 'h2']],
        'a canceled waiter ends, the other one wakes'))
    out.append(s('to_thread_value', {}, [['to_thread', 5], ['log', 'after']], 'to_thread returns the value'))
    out.append(s('call_chain_hops', {
        'bg': bg, 'a': [['log', 's'], ['call', 'b'], ['log', 'e'], ['return', 1]], 'b': steps('b', 2) + [['return', 2]],
    }, [['create_task', 'bg', 't'], ['call', 'a'], ['log', 'after'], ['await', 't']],
        'await of a coroutine runs in the same task with no extra trip'))
    out.append(s('heartbeat_cleanup', {
        'beat': [['try', steps('beat', 8), 'reraise']],
        'work': steps('work', 2) + [['return', 1]],
    }, [['create_task', 'beat', 'hb'], ['call', 'work'], ['cancel', 'hb'], ['gather_tasks', ['hb'], True],
        ['log', 'after']], 'task.cancel(); await gather(task, return_exceptions=True) of phases.py and scheduler.py'))
    out.append(s('gather_tasks_return_exceptions', {
        'x': [['yield'], ['raise', 'ValueError', 'x']], 'y': [['return', 2]],
    }, [['create_task', 'x', 'hx'], ['create_task', 'y', 'hy'], ['gather_tasks', ['hx', 'hy'], True]],
        'gather of tasks with return_exceptions=True'))
    out.append(s('await_failed_task', {'t': [['yield'], ['raise', 'RuntimeError', 'task failed']]},
        [['create_task', 't', 'h'], ['try', [['await', 'h']], 'swallow'], ['log', 'after']],
        'await of a task that raised'))
    out.append(s('gather_child_gets_cancelled_error', {
        'victim': steps('victim', 6),
        'a': [['await', 'hv']], 'b': steps('b', 3) + [['return', 2]],
    }, [['create_task', 'victim', 'hv'], ['yield'], ['cancel', 'hv'],
        ['try', [['gather', ['a', 'b'], False]], 'all'], ['yield'], ['yield'], ['yield'], ['yield'], ['log', 'after']],
        'a call that gets CancelledError from an awaited task ends the gather with CancelledError'))
    for rx in (False, True):
        out.append(s(f'gather_existing_task_hops_{"rx" if rx else "plain"}', {
            'bg': steps('bg', 9) + [['log', 'end']], 'inner': steps('inner', 2) + [['log', 'done'], ['return', 4]],
        }, [['create_task', 'bg', 't'], ['create_task', 'inner', 'h'], ['gather_tasks', ['h'], rx], ['log', 'gathered'],
            ['await', 't']], 'the trips of a gather of a task that exists, seen from a task that runs beside it'))
    out.append(s('gather_existing_tasks_failure_hops', {
        'bg': steps('bg', 9) + [['log', 'end']], 'x': [['yield'], ['raise', 'ValueError', 'x']], 'y': steps('y', 4),
    }, [['create_task', 'bg', 't'], ['create_task', 'x', 'hx'], ['create_task', 'y', 'hy'],
        ['try', [['gather_tasks', ['hx', 'hy'], False]], 'swallow'], ['log', 'gathered'], ['await', 't']],
        'the first failure of a gather of tasks, against a task beside it'))
    out.append(s('gather_existing_done_task_hops', {
        'bg': steps('bg', 6) + [['log', 'end']], 'inner': [['return', 1]],
    }, [['create_task', 'inner', 'h'], ['yield'], ['yield'], ['create_task', 'bg', 't'], ['gather_tasks', ['h'], True],
        ['log', 'gathered'], ['await', 't']], 'a gather of a task that has ended'))
    return out


def known_differences() -> list[dict]:
    """Scripts where the port differs from asyncio by a rule of R8. The trace here is the asyncio trace. The C# test
    asserts the trace of the helpers and names this one in a comment, so that each difference stays known."""
    return [
        s('differs_cleanup_after_cancel', {
            't': [['try', [['ev_wait', 'never']], 'reraise', [['to_thread', 1]]]],
        }, [['create_task', 't', 'h'], ['yield'], ['cancel', 'h'], ['try', [['await', 'h']], 'all'], ['log', 'after']],
            'except BaseException: await to_thread(f). The cancellation of asyncio is delivered once, so the cleanup '
            'runs. A canceled token stays canceled, so the cleanup that passes it ends at once.'),
        s('differs_cancel_wake_order', {
            't': [['gather', ['w1', 'w2', 'w3'], False]],
            'w1': [['try', [['ev_wait', 'e']], 'reraise']],
            'w2': [['try', [['ev_wait', 'e']], 'reraise']],
            'w3': [['try', [['ev_wait', 'e']], 'reraise']],
        }, [['create_task', 't', 'h'], ['yield'], ['yield'], ['cancel', 'h'], ['try', [['await', 'h']], 'all']],
            'one cancellation of three waiters: asyncio wakes them in order, a token in reverse order of registration'),
    ]


def random_body(rng: random.Random, i: int, n: int, with_events: bool) -> list:
    ops: list = []
    for k in range(rng.randint(1, 5)):
        ops.append(['log', f'{k}'] if rng.random() < 0.5 else ['yield'])
    later = [f'c{j}' for j in range(i + 1, n)]
    if later and rng.random() < 0.5:
        pick = rng.sample(later, rng.randint(1, len(later)))
        choice = rng.random()
        if choice < 0.35:
            ops.append(['gather', pick, rng.random() < 0.3])
        elif choice < 0.7:
            ops.append(['sgather', pick, rng.choice([None, 0, 1, 2, 3])])
        else:
            ops.append(['call', pick[0]])
        if rng.random() < 0.5:
            ops = [['try', ops, 'swallow']]
    if with_events and rng.random() < 0.3:
        ops.insert(rng.randint(0, len(ops)), ['ev_set', 'e'] if rng.random() < 0.5 else ['ev_wait', 'e'])
    end = rng.random()
    if end < 0.65:
        ops.append(['return', i * 10 + rng.randint(0, 9)])
    elif end < 0.85:
        ops.append(['raise', rng.choice(['ValueError', 'RuntimeError']), f'c{i} failed'])
    return ops


def random_script(rng: random.Random, index: int) -> dict:
    n = rng.randint(2, 7)
    with_events = rng.random() < 0.3
    coros = {f'c{i}': random_body(rng, i, n, with_events) for i in range(n)}
    roots = [f'c{i}' for i in range(n) if rng.random() < 0.7] or ['c0']
    main: list = [['log', 'start']]
    shape = rng.random()
    if shape < 0.3:
        main.append(['try', [['gather', roots, rng.random() < 0.3]], 'swallow'])
    elif shape < 0.6:
        main.append(['try', [['sgather', roots, rng.choice([None, 1, 2, 3, 4])]], 'swallow'])
    elif shape < 0.75:
        for j, r in enumerate(roots):
            main.append(['create_task', r, f'h{j}'])
        main += [['yield']] * rng.randint(0, 3)
        for j in range(len(roots)):
            main.append(['try', [['await', f'h{j}']], 'swallow'])
    elif shape < 0.88 and not with_events:
        # A cancellation at a random yield. Bodies with events are left out: a canceled event waiter in Python
        # and in C# wake in a different order when two of them share one cancellation (see AsyncioTests).
        main += [['create_task', roots[0], 'h'], *[['yield']] * rng.randint(0, 6), ['cancel', 'h'],
                 ['try', [['await', 'h']], 'all']]
    else:
        main.append(['try', [['wait_for', roots[0], 10.0]], 'swallow'])
    main += [['yield']] * rng.randint(0, 4)
    main.append(['log', 'end'])
    return s(f'random_{index:03d}', coros, main, f'seed {SEED}, script {index}')


def build_values() -> dict:
    scripts = []
    for script in hand_scripts():
        trace = run_script(script)
        scripts.append({**script, 'trace': trace})
    rng = random.Random(SEED)
    index = 0
    while index < RANDOM_SCRIPTS:
        script = random_script(rng, index)
        trace = run_script(script, guard=0.5)
        if trace is None:
            continue
        scripts.append({**script, 'trace': trace})
        index += 1
    return {
        'meta': {'python': sys.version.split()[0], 'seed': SEED, 'semaphore_limit': helpers.SEMAPHORE_LIMIT,
                 'timeout_error_text': str(TimeoutError()),
                 'note': 'Generated by port/tools/generate_asyncio_values.py. Do not edit.'},
        'scripts': scripts,
        'differs': [{**script, 'trace': run_script(script)} for script in known_differences()],
    }


def main() -> int:
    check = '--check' in sys.argv[1:]
    text = json.dumps(build_values(), indent=1, ensure_ascii=True) + '\n'
    if check:
        if not VALUES_PATH.exists() or VALUES_PATH.read_text(encoding='utf-8') != text:
            print(f'stale: {VALUES_PATH.relative_to(ROOT)}')
            return 1
        print('generate_asyncio_values: up to date')
        return 0
    VALUES_PATH.write_text(text, encoding='utf-8', newline='\n')
    print(f'wrote {VALUES_PATH.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
