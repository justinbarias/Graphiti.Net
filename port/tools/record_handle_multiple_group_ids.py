"""Records results of graphiti_core/decorators.py (handle_multiple_group_ids) from real Python, for the evaluation tests.

The upstream test file checks six calls with one result shape. This file runs the wrapper over each branch
(no group ids, an empty list, one id equal to the database of the driver, one other id, two ids, three ids,
two equal ids) with each result shape (list, SearchResults, tuple of two lists), each provider (FalkorDB, Neo4j),
a host with no `clients`, a caller driver or none, and max_coroutines of None or 1 (port/RULES.md, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_handle_multiple_group_ids.py
    .upstream/graphiti/.venv/bin/python port/tools/record_handle_multiple_group_ids.py --check

It writes tests/Port.Evaluation/values/handle_multiple_group_ids.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import sys
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
# GRAPHITI_UPSTREAM names another checkout of the pinned upstream, for a worktree that has no .upstream.
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'handle_multiple_group_ids.json'

GROUPS = [None, [], ['reggraph'], ['other'], ['a', 'b'], ['a', 'b', 'c'], ['a', 'a']]
SHAPES = ['list', 'search', 'tuple']
PROVIDERS = ['falkordb', 'neo4j', 'none']


async def run_case(decorator, GraphProvider, SearchResults, provider, shape, group_ids, caller, max_coroutines):
    class FakeDriver:
        def __init__(self, database, prov):
            self._database = database
            self.provider = prov
            self.clone_calls = []

        def clone(self, database):
            self.clone_calls.append(database)
            return FakeDriver(database, self.provider)

    seen = []

    class Host:
        def __init__(self, driver):
            if driver is not None:
                self.clients = SimpleNamespace(driver=driver)
            self.max_coroutines = max_coroutines

        @decorator
        async def run(self, group_ids=None, driver=None):
            # The C# fake yields before it records the call (port/RULES.md, R25). This one does the same.
            await asyncio.sleep(0)
            db = getattr(driver, '_database', None) if driver is not None else None
            seen.append([group_ids, db])
            ids = group_ids or []
            if shape == 'list':
                return [f'{group_ids}:{db}']
            if shape == 'search':
                return SearchResults(
                    edge_reranker_scores=[float(sum(ord(c) for c in ''.join(ids)))],
                    node_reranker_scores=[float(len(ids))],
                )
            return ([f'a:{group_ids}:{db}'], [db or '-'])

    clients_driver = None if provider == 'none' else FakeDriver('reggraph', GraphProvider.FALKORDB if provider == 'falkordb' else GraphProvider.NEO4J)
    host = Host(clients_driver)
    kwargs = {'group_ids': group_ids}
    if caller:
        kwargs['driver'] = FakeDriver('caller', GraphProvider.NEO4J)
    result = await host.run(**kwargs)
    if shape == 'search':
        result = {'edge': result.edge_reranker_scores, 'node': result.node_reranker_scores}
    elif shape == 'tuple':
        result = [result[0], result[1]]
    return {'result': result, 'seen': seen, 'clone_calls': [] if clients_driver is None else clients_driver.clone_calls}


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    from graphiti_core.decorators import handle_multiple_group_ids
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.search.search_config import SearchResults

    cases = []
    for provider in PROVIDERS:
        for shape in SHAPES:
            for group_ids in GROUPS:
                for caller in (False, True):
                    for max_coroutines in (None, 1):
                        out = asyncio.run(run_case(handle_multiple_group_ids, GraphProvider, SearchResults,
                                                   provider, shape, group_ids, caller, max_coroutines))
                        cases.append({'provider': provider, 'shape': shape, 'group_ids': group_ids, 'caller': caller,
                                      'max': max_coroutines, **out})
    lines = [json.dumps(c, ensure_ascii=True, separators=(',', ':')) for c in cases]
    text = ('{"python":"' + '.'.join(str(p) for p in sys.version_info[:3])
            + '","source":"graphiti_core/decorators.py","cases":[\n' + ',\n'.join(lines) + '\n]}\n')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(cases)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(cases)} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
