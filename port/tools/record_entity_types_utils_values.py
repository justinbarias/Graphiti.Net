"""Records results of graphiti_core/utils/ontology_utils/entity_types_utils.py from real Python, for the evaluation tests.

The judge has 15 cases for this module, and the upstream test checks four invalid configurations. Neither
covers the order of the checks, or which field name a protected-name error names. This file gives the
evaluation tests about 600 calls with generated inputs from a fixed seed (port/RULES.md, R23 and R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_entity_types_utils_values.py
    .upstream/graphiti/.venv/bin/python port/tools/record_entity_types_utils_values.py --check

It writes tests/Port.Evaluation/values/entity_types_utils.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.

Each case holds the function name, the arguments and the result. A custom type is a list of field names
(each field is a `str`), and the evaluation test builds a descriptor from it. The result is `{"ok": true}` or
`{"err": [class name, message]}`. The recording leaves out an identity property that is not a list, because
the C# parameter type cannot hold it (the judge cases cover it).
"""
from __future__ import annotations

import json
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'entity_types_utils.json'

sys.path.insert(0, str(UPSTREAM))

# The 8 names of EntityNode.model_fields are protected. The others are free.
PROTECTED = ['uuid', 'name', 'group_id', 'labels', 'created_at', 'name_embedding', 'summary', 'attributes']
FREE = ['email', 'phone', 'age', 'full_name', 'Name', 'UUID', 'summary_text', 'nameX', 'x', 'é', 'attribute']
TYPE_NAMES = ['Person', 'Company', 'Thing', 'A', 'B', 'Émile', 'Type 1', '']

rng = random.Random(20261127)
records: list[dict] = []


def rand_types(allow_none: bool = True):
    if allow_none and rng.random() < 0.1:
        return None
    names = rng.sample(TYPE_NAMES, rng.randint(0, 4))
    out = {}
    for n in names:
        pool = FREE + (PROTECTED if rng.random() < 0.3 else [])
        out[n] = rng.sample(pool, rng.randint(0, 4))
    return out


def rand_props(types):
    if rng.random() < 0.08:
        return None
    names = list(types) if types else []
    out = {}
    for _ in range(rng.randint(0, 3)):
        r = rng.random()
        if names and r < 0.75:
            n = rng.choice(names)
        else:
            n = rng.choice(TYPE_NAMES)
        fields = types[n] if types and n in types else []
        r = rng.random()
        if r < 0.15:
            plist = []
        elif r < 0.45 and fields:
            plist = rng.sample(fields, rng.randint(1, len(fields)))
        elif r < 0.6:
            plist = rng.sample(fields, len(fields)) + [rng.choice(FREE)] if fields else [rng.choice(FREE)]
        else:
            plist = [rng.choice(FREE + PROTECTED)]
        out[n] = plist
    return out


def make_models(types):
    from pydantic import create_model

    if types is None:
        return None
    return {n: create_model('T', **{f: (str, ...) for f in fields}) for n, fields in types.items()}


def call(fn, *args):
    try:
        fn(*args)
        return {'ok': True}
    except Exception as e:  # noqa: BLE001
        return {'err': [type(e).__name__, str(e)]}


def main() -> int:
    from graphiti_core.utils.ontology_utils import entity_types_utils as m

    for _ in range(300):
        types = rand_types()
        records.append({'f': 'validate_entity_types', 'a': {'types': types},
                        'r': call(m.validate_entity_types, make_models(types))})
    for _ in range(300):
        types = rand_types(allow_none=False) if rng.random() < 0.92 else None
        props = rand_props(types)
        records.append({'f': 'validate_identity_properties', 'a': {'props': props, 'types': types},
                        'r': call(m.validate_identity_properties, props, make_models(types))})

    lines = [json.dumps(r, ensure_ascii=True, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3])
        + '","source":"graphiti_core/utils/ontology_utils/entity_types_utils.py","cases":[\n'
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
