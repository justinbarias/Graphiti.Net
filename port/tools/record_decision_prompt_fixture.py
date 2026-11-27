"""Records the prompt fixture of the upstream decision client tests (port/RULES.md, R25).

The upstream test test_shared_prompt_renders_match_zep_fixture_for_both_clients compares two rendered prompts with
tests/llm_client/decision_prompt_fixture.json. The ported test needs the same expected texts. This tool reads the
fixture of the pinned upstream and writes its `node` text and its `edge` text.

    .upstream/graphiti/.venv/bin/python port/tools/record_decision_prompt_fixture.py
    .upstream/graphiti/.venv/bin/python port/tools/record_decision_prompt_fixture.py --check

It writes tests/Port.Evaluation/values/decision_prompt_fixture.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The environment variable GRAPHITI_UPSTREAM names the upstream checkout, and
it defaults to .upstream/graphiti in the repository. The tool changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', ROOT / '.upstream' / 'graphiti'))
FIXTURE = UPSTREAM / 'tests' / 'llm_client' / 'decision_prompt_fixture.json'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'decision_prompt_fixture.json'


def main() -> int:
    fixture = json.loads(FIXTURE.read_text(encoding='utf-8'))
    assert sorted(fixture) == ['edge', 'node'], sorted(fixture)
    text = json.dumps({'node': fixture['node'], 'edge': fixture['edge']}, ensure_ascii=False, indent=1) + '\n'
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, 2 texts')
        return 0
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: wrote 2 texts')
    return 0


if __name__ == '__main__':
    sys.exit(main())
