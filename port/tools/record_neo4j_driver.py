"""Records what Python Neo4jDriver does with a fake client (plan 0004, layer 5, packet P5a, review round).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_neo4j_driver.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_neo4j_driver.py --check

The tool replaces the client of graphiti_core/driver/neo4j_driver.py with a fake at the network boundary, so it needs
no database. Each fake method runs `await asyncio.sleep(0)` first and then records its name, as the fakes of the
port do. It records:

- index_queries: the queries of build_indices_and_constraints, with delete_existing False and True, in the order in
  which the fake client receives them. This is get_range_indices(NEO4J) + get_fulltext_indices(NEO4J), and the delete
  query first when delete_existing is True.
- execute_calls: what execute_query passes to client.execute_query for a set of calls (the parameters, the database,
  and the other keyword names).
- index_race: a ClientError with the code EquivalentSchemaRuleAlreadyExists for one index query gives no error and a
  debug line. A ClientError with another code passes on. The log lines are in one ordered list with the calls.
- transactions: the order of the calls of the fake client, the session, and the transaction for a commit, a body that
  raises, and a body that is canceled.
- sessions: the database of driver.session(...) and of transaction() for some drivers, and the close of the driver.
- health_check: the printed text and the error type for a fake client whose verify_connectivity fails.
- error_texts: str(e) of the errors that the fakes raise.

It writes tests/Graphiti.Net.E2E.Tests/values/neo4j_driver.json. --check writes nothing and exits 1 if the file
differs from what the run would write. It reads the upstream folder from the environment variable GRAPHITI_UPSTREAM,
then from .upstream/graphiti in the repository, then from the main checkout when this is a git worktree.
It opens no connection and touches no database.
"""
from __future__ import annotations

import asyncio
import contextlib
import io
import json
import logging
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'tests' / 'Graphiti.Net.E2E.Tests' / 'values' / 'neo4j_driver.json'


def find_upstream() -> Path:
    given = os.environ.get('GRAPHITI_UPSTREAM')
    if given:
        return Path(given)
    local = ROOT / '.upstream' / 'graphiti'
    if local.exists():
        return local
    # A git worktree has no .upstream of its own. The main checkout holds it.
    return ROOT.parents[2] / '.upstream' / 'graphiti'


UPSTREAM = find_upstream()
sys.path.insert(0, str(UPSTREAM))

from neo4j.exceptions import Neo4jError, ServiceUnavailable  # noqa: E402

from graphiti_core.driver import neo4j_driver as module  # noqa: E402
from graphiti_core.driver.neo4j_driver import Neo4jDriver  # noqa: E402

RACE_CODE = 'Neo.ClientError.Schema.EquivalentSchemaRuleAlreadyExists'
OTHER_CODE = 'Neo.ClientError.Schema.IndexWithNameAlreadyExists'
RACE_QUERY_PREFIX = 'CREATE INDEX entity_uuid IF NOT EXISTS'


def server_error(code: str, message: str) -> Neo4jError:
    """An error as the server sends it, with the empty GQL status that the .NET ClientException(code, message) has."""
    return Neo4jError._hydrate_gql(neo4j_code=code, message=message, gql_status='', description='')


class Recorder:
    """One ordered list of the calls of the fakes and the log lines."""

    def __init__(self) -> None:
        self.events: list = []
        self.calls: list = []

    def add(self, name: str) -> None:
        self.events.append(name)


class FakeTx:
    def __init__(self, rec: Recorder) -> None:
        self.rec = rec

    async def run(self, query, **kwargs):
        await asyncio.sleep(0)
        self.rec.add('run')

    async def commit(self):
        await asyncio.sleep(0)
        self.rec.add('commit')

    async def rollback(self):
        await asyncio.sleep(0)
        self.rec.add('rollback')


class FakeSession:
    def __init__(self, rec: Recorder) -> None:
        self.rec = rec

    async def __aenter__(self):
        return self

    async def __aexit__(self, *exc):
        await self.close()

    async def begin_transaction(self):
        await asyncio.sleep(0)
        self.rec.add('begin_transaction')
        return FakeTx(self.rec)

    async def close(self):
        await asyncio.sleep(0)
        self.rec.add('close')


class FakeClient:
    def __init__(self, rec: Recorder, fail=None) -> None:
        self.rec = rec
        self.fail = fail or {}
        self.verify_error = None

    def session(self, database=None):
        self.rec.add(f'session(database={database})')
        return FakeSession(self.rec)

    async def execute_query(self, query, parameters_=None, database_=None, **kwargs):
        await asyncio.sleep(0)
        self.rec.calls.append(
            {
                'query': query,
                'parameters_': parameters_,
                'database_': database_,
                'other_keys': list(kwargs),
                'other': {k: v for k, v in kwargs.items()},
            }
        )
        error = None
        for prefix, candidate in self.fail.items():
            if query.startswith(prefix):
                error = candidate
        if error is not None:
            raise error
        return None

    async def verify_connectivity(self):
        await asyncio.sleep(0)
        if self.verify_error is not None:
            raise self.verify_error

    async def close(self):
        await asyncio.sleep(0)
        self.rec.add('client.close')


class LogCapture(logging.Handler):
    def __init__(self, rec: Recorder) -> None:
        super().__init__(level=logging.DEBUG)
        self.rec = rec

    def emit(self, record):
        self.rec.events.append([record.levelname, record.getMessage()])


def make_driver(rec: Recorder, database: str = 'neo4j', fail=None) -> tuple[Neo4jDriver, FakeClient]:
    # Built outside a running loop, so that the constructor starts no index build.
    driver = Neo4jDriver('bolt://localhost:1', 'u', 'p', database)
    client = FakeClient(rec, fail)
    driver.client = client
    return driver, client


@contextlib.contextmanager
def capture(rec: Recorder):
    handler = LogCapture(rec)
    logger = module.logger
    previous = logger.level
    logger.setLevel(logging.DEBUG)
    logger.addHandler(handler)
    propagate = logger.propagate
    logger.propagate = False
    try:
        yield
    finally:
        logger.removeHandler(handler)
        logger.setLevel(previous)
        logger.propagate = propagate


def run(coro):
    return asyncio.run(coro)


def record_index_queries() -> dict:
    out = {}
    for name, delete_existing in (('default', False), ('delete_existing', True)):
        rec = Recorder()
        driver, _ = make_driver(rec)
        run(driver.build_indices_and_constraints(delete_existing=delete_existing))
        out[name] = [c['query'] for c in rec.calls]
    return out


def record_execute_calls() -> list:
    cases = [
        ('plain', 'RETURN 1', {}),
        ('params', 'RETURN $a', {'params': {'a': 1}}),
        ('merge', 'RETURN $a, $b', {'params': {'a': 1, 'b': 2}, 'b': 3, 'c': None}),
        ('database_none', 'RETURN 1', {'database_': None}),
        ('database_named', 'RETURN 1', {'database_': 'other'}),
        ('routing_r', 'RETURN 1', {'routing_': 'r'}),
        ('routing_w', 'RETURN 1', {'routing_': 'w'}),
    ]
    out = []
    for name, query, kwargs in cases:
        rec = Recorder()
        driver, _ = make_driver(rec)
        run(driver.execute_query(query, **kwargs))
        call = rec.calls[0]
        out.append(
            {
                'name': name,
                'query': call['query'],
                'parameters': call['parameters_'],
                'database': call['database_'],
                'other': call['other'],
            }
        )
    return out


def record_index_race() -> dict:
    out = {}
    for name, error in (
        ('equivalent_exists', server_error(RACE_CODE, 'An equivalent index already exists')),
        ('other_code', server_error(OTHER_CODE, 'There already exists an index called entity_uuid')),
    ):
        rec = Recorder()
        driver, client = make_driver(rec, fail={RACE_QUERY_PREFIX: error})
        raised = None
        with capture(rec):
            try:
                run(driver.build_indices_and_constraints())
            except Exception as e:  # noqa: BLE001
                raised = {'type': type(e).__name__, 'text': str(e)}
        out[name] = {
            'raised': raised,
            'log': [e for e in rec.events if isinstance(e, list)],
            'queries': sorted(c['query'] for c in rec.calls),
        }
    return out


def record_transactions() -> dict:
    class BodyError(Exception):
        pass

    async def commit_case(driver):
        async with driver.transaction() as tx:
            await tx.run('Q', a=1)

    async def raise_case(driver):
        async with driver.transaction() as tx:
            await tx.run('Q', a=1)
            raise BodyError('body failed')

    async def cancel_case(driver):
        async with driver.transaction() as tx:
            await tx.run('Q', a=1)
            raise asyncio.CancelledError()

    out = {}
    for name, case in (('commit', commit_case), ('body_throws', raise_case), ('body_canceled', cancel_case)):
        rec = Recorder()
        driver, _ = make_driver(rec)
        outcome = None
        try:
            run(case(driver))
        except BaseException as e:  # noqa: BLE001
            outcome = type(e).__name__
        out[name] = {'calls': rec.events, 'outcome': outcome}
    return out


def record_sessions() -> dict:
    out = {}
    rec = Recorder()
    driver, _ = make_driver(rec)
    driver.session()
    driver.session('other')
    driver.session('')
    driver.session(None)
    out['default_driver'] = list(rec.events)

    rec = Recorder()
    driver, _ = make_driver(rec, database='graphiti')

    async def use():
        driver.session()
        driver.session('other')
        async with driver.transaction():
            pass

    run(use())
    out['named_driver'] = list(rec.events)

    rec = Recorder()
    driver, _ = make_driver(rec)
    run(driver.close())
    out['driver_close'] = list(rec.events)
    return out


def record_health_check() -> list:
    out = []
    for name, error in (
        ('ok', None),
        ('server_error', server_error('Neo.ClientError.Security.Unauthorized', 'The client is unauthorized.')),
        ('driver_error', ServiceUnavailable('Unable to connect')),
    ):
        rec = Recorder()
        driver, client = make_driver(rec)
        client.verify_error = error
        buffer = io.StringIO()
        raised = None
        with contextlib.redirect_stdout(buffer):
            try:
                run(driver.health_check())
            except Exception as e:  # noqa: BLE001
                raised = type(e).__name__
        out.append({'name': name, 'printed': buffer.getvalue(), 'raised': raised})
    return out


def record_error_texts() -> list:
    rec = Recorder()
    driver, _ = make_driver(rec, fail={'RETURN': server_error('Neo.ClientError.Test', 'recorded')})
    with capture(rec):
        try:
            run(driver.execute_query('RETURN 1', params={'a': 1}, zeta=2, routing_='r'))
        except Exception as e:  # noqa: BLE001
            text = str(e)
    return [{'case': 'execute_query', 'text': text, 'log': [e for e in rec.events if isinstance(e, list)]}]


def build() -> dict:
    return {
        'index_queries': record_index_queries(),
        'execute_calls': record_execute_calls(),
        'index_race': record_index_race(),
        'transactions': record_transactions(),
        'sessions': record_sessions(),
        'health_check': record_health_check(),
        'error_texts': record_error_texts(),
    }


def main() -> int:
    text = json.dumps(build(), indent=1, ensure_ascii=False) + '\n'
    if '--check' in sys.argv:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else None
        if current == text:
            print('current')
            return 0
        print('stale: run the tool without --check')
        return 1
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'wrote {OUT.relative_to(ROOT)}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
