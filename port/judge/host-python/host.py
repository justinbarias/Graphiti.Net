"""The Python judge host. It runs inside the upstream virtual environment.

It reads one case as a JSON line from standard input and prints one JSON result line.
It decodes the case, calls the real upstream code with exactly the arguments given, and
prints the result or the exception's class name. It adds no defaults and no checks.

Nothing but result lines may reach standard output. Logs go to standard error.
"""
from __future__ import annotations

import asyncio
import inspect
import json
import logging
import os
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

HERE = Path(__file__).resolve().parent
JUDGE = HERE.parent
ROOT = JUDGE.parent.parent
sys.path.insert(0, str(HERE))
# The break tests point the host at a modified copy of upstream. Nothing else sets this variable.
sys.path.insert(0, os.environ.get('JUDGE_UPSTREAM_DIR') or str(ROOT / '.upstream' / 'graphiti'))

os.environ.setdefault('GRAPHITI_TELEMETRY_ENABLED', 'false')
logging.basicConfig(stream=sys.stderr, level=logging.WARNING)

from codec import decode, encode, resolve  # noqa: E402

VERBOSE = bool(os.environ.get('JUDGE_VERBOSE'))


def _load_dotenv() -> None:
    """Reads KEY=VALUE lines from the repository .env file. Values are never printed."""
    path = ROOT / '.env'
    if not path.exists():
        return
    for line in path.read_text(encoding='utf-8').splitlines():
        line = line.strip()
        if line and not line.startswith('#') and '=' in line:
            key, _, value = line.partition('=')
            os.environ.setdefault(key.strip(), value.strip().strip('"').strip("'"))


# --------------------------------------------------------------------------- judge functions


def _schema_text(model_path: str) -> str:
    """The exact expression that upstream `LLMClient.generate_response` puts into a prompt."""
    return json.dumps(resolve(model_path).model_json_schema())


def _embed(text: str) -> list[float]:
    from judge_clients import judge_embedding

    return judge_embedding(text)


JUDGE_FUNCTIONS = {'judge:schema_text': _schema_text, 'judge:embed': _embed}


# --------------------------------------------------------------------------- pure layer


async def _finish(value: Any) -> Any:
    if inspect.isawaitable(value):
        value = await value
    if inspect.isgenerator(value):
        value = list(value)
    if inspect.isasyncgen(value):
        value = [item async for item in value]
    return value


async def run_pure(case: dict) -> dict:
    try:
        fn = JUDGE_FUNCTIONS.get(case['fn']) or resolve(case['fn'])
        args = decode(case.get('args', []))
        kwargs = decode(case.get('kwargs', {}))
        return {'ok': encode(await _finish(fn(*args, **kwargs)))}
    except Exception as error:  # noqa: BLE001
        if VERBOSE:
            traceback.print_exc(file=sys.stderr)
        return {'error': type(error).__name__}


# --------------------------------------------------------------------------- flow layer


def _lookup(value: Any, path: str) -> Any:
    for part in filter(None, path.split('.')):
        if isinstance(value, (list, tuple)):
            value = value[int(part)]
        elif isinstance(value, dict):
            value = value[part]
        else:
            value = getattr(value, part)
    return value


def _decode_with_steps(value: Any, returns: list[Any], graphiti: Any = None) -> Any:
    """Like decode, plus three tags that only flow cases use.

    {"$step": [index, "path"]}  a value from the return of an earlier step
    {"$graphiti": "path"}       an object on the Graphiti instance, for example "driver"
    {"$embed": "text"}          the judge embedding of the text
    """
    if isinstance(value, dict) and '$step' in value:
        index, path = value['$step']
        return _lookup(returns[index], path)
    if isinstance(value, dict) and '$graphiti' in value:
        return _lookup(graphiti, value['$graphiti'])
    if isinstance(value, dict) and '$embed' in value:
        return _embed(value['$embed'])
    if isinstance(value, dict) and '$model' in value:
        data = _decode_with_steps(value.get('data', {}), returns, graphiti)
        return resolve(value['$model'])(**data)
    if isinstance(value, dict) and not any(k.startswith('$') for k in value):
        return {k: _decode_with_steps(v, returns, graphiti) for k, v in value.items()}
    if isinstance(value, list):
        return [_decode_with_steps(v, returns, graphiti) for v in value]
    return decode(value)


async def _dump_graph(client: Any, groups: list[str]) -> dict:
    nodes, _, _ = await client.execute_query(
        'MATCH (n) WHERE n.group_id IN $groups RETURN labels(n) AS labels, properties(n) AS props',
        parameters_={'groups': groups},
    )
    edges, _, _ = await client.execute_query(
        'MATCH (a)-[r]->(b) WHERE r.group_id IN $groups OR a.group_id IN $groups '
        'RETURN type(r) AS type, a.uuid AS source, b.uuid AS target, properties(r) AS props',
        parameters_={'groups': groups},
    )
    return {
        'nodes': [{'labels': sorted(r['labels']), 'props': encode(dict(r['props']))} for r in nodes],
        'edges': [
            {'type': r['type'], 'source': r['source'], 'target': r['target'], 'props': encode(dict(r['props']))}
            for r in edges
        ],
    }


async def _delete_groups(client: Any, groups: list[str]) -> None:
    await client.execute_query('MATCH (n) WHERE n.group_id IN $groups DETACH DELETE n', parameters_={'groups': groups})


class JudgeDatabaseNotEmpty(Exception):
    pass


async def _prepare_database(driver: Any, client: Any, groups: list[str]) -> None:
    """Gives the case an empty database with new full-text indexes.

    A full-text score depends on every entry of the index, also on entries of deleted nodes
    that Neo4j did not remove from the index yet. With an index that other cases used, the
    order of search results changes between runs. The .NET host does the same steps.
    """
    await _delete_groups(client, groups)
    rows, _, _ = await client.execute_query('MATCH (n) RETURN count(n) AS count')
    if rows[0]['count']:
        print(
            f"judge: the database holds {rows[0]['count']} nodes outside the groups of this case. "
            'The judge needs a database of its own: no other test or tool can use it at the same time.',
            file=sys.stderr,
        )
        raise JudgeDatabaseNotEmpty()
    names, _, _ = await client.execute_query('SHOW FULLTEXT INDEXES YIELD name RETURN name')
    for row in names:
        await client.execute_query(f"DROP INDEX `{row['name']}` IF EXISTS")
    await driver.build_indices_and_constraints()
    await client.execute_query('CALL db.awaitIndexes(120)')


async def run_flow(case: dict) -> dict:
    from judge_clients import JudgeCrossEncoder, JudgeEmbedder, JudgeLLMClient, Recorder, RecordingNeo4jClient

    from graphiti_core import Graphiti
    from graphiti_core.driver.neo4j_driver import Neo4jDriver

    recorder = Recorder()
    groups = [case['group'], *case.get('extra_groups', [])]
    replies_path = JUDGE / 'replies' / f"{case['replies']}.jsonl" if case.get('replies') else None

    inner = None
    if os.environ.get('JUDGE_RECORD_REPLIES') and replies_path is not None:
        _load_dotenv()
        from graphiti_core.llm_client.config import LLMConfig
        from graphiti_core.llm_client.openai_generic_client import OpenAIGenericClient

        inner = OpenAIGenericClient(
            config=LLMConfig(
                api_key=os.environ['OPENAI_API_KEY'],
                base_url=os.environ.get('OPENAI_BASE_URL'),
                model=os.environ.get('JUDGE_RECORD_MODEL', 'gpt-4.1-mini'),
                temperature=0,
            )
        )

    driver = Neo4jDriver(
        os.environ.get('NEO4J_URI', 'bolt://localhost:7687'),
        os.environ.get('NEO4J_USER', 'neo4j'),
        os.environ.get('NEO4J_PASSWORD', 'password'),
    )
    raw_client = driver.client
    # The constructor schedules an index build. The judge does not capture it: scenarios call
    # build_indices_and_constraints as an explicit step.
    if driver._init_task is not None:
        await driver._init_task
    await _prepare_database(driver, raw_client, groups)
    driver.client = RecordingNeo4jClient(raw_client, recorder)

    graphiti = Graphiti(
        graph_driver=driver,
        llm_client=JudgeLLMClient(recorder, replies_path, inner),
        embedder=JudgeEmbedder(recorder),
        cross_encoder=JudgeCrossEncoder(recorder),
        **decode(case.get('graphiti', {})),
    )

    steps: list[dict] = []
    returns: list[Any] = []
    missing: list = []
    try:
        for step in case['steps']:
            recorder.reset()
            entry: dict = {'call': step.get('call', 'parallel')}
            try:
                if 'parallel' in step:
                    # Calls that start together. The result keeps the order of the list.
                    calls = [
                        _finish(
                            _lookup(graphiti, part['call'])(
                                *_decode_with_steps(part.get('args', []), returns, graphiti),
                                **_decode_with_steps(part.get('kwargs', {}), returns, graphiti),
                            )
                        )
                        for part in step['parallel']
                    ]
                    value = await asyncio.gather(*calls)
                else:
                    target = _lookup(graphiti, step['call'])
                    args = _decode_with_steps(step.get('args', []), returns, graphiti)
                    kwargs = _decode_with_steps(step.get('kwargs', {}), returns, graphiti)
                    value = await _finish(target(*args, **kwargs))
                returns.append(value)
                entry['ret'] = encode(value)
            except Exception as error:  # noqa: BLE001
                if VERBOSE:
                    traceback.print_exc(file=sys.stderr)
                returns.append(None)
                entry['error'] = type(error).__name__
            entry.update(recorder.take())
            missing.extend(recorder.missing)
            steps.append(entry)
        graph = await _dump_graph(raw_client, groups)
    finally:
        await _delete_groups(raw_client, groups)
        await raw_client.close()

    result: dict = {
        'steps': steps,
        'graph': graph,
        'meta': {'start': recorder.started.isoformat(), 'end': datetime.now(timezone.utc).isoformat()},
    }
    if missing:
        result['missing_replies'] = missing
    if recorder.recording_failed:
        result['recording_failed'] = sorted(set(recorder.recording_failed))
    return result


# --------------------------------------------------------------------------- loop


async def main() -> None:
    # Upstream code and its libraries may print. Only result lines may reach standard output.
    out = sys.stdout
    sys.stdout = sys.stderr
    for line in sys.stdin:
        if not line.strip():
            continue
        case = json.loads(line)
        try:
            result = await (run_flow(case) if 'steps' in case else run_pure(case))
        except Exception as error:  # noqa: BLE001
            traceback.print_exc(file=sys.stderr)
            result = {'host_error': type(error).__name__}
        out.write(json.dumps(result, ensure_ascii=True) + '\n')
        out.flush()


if __name__ == '__main__':
    asyncio.run(main())
