"""Records the message of each exception class in graphiti_core/errors.py, from real Python.

The judge compares exception class names only (port/GAPS.md, J1). This file gives the
evaluation tests the text that each class produces, so that the port can be compared with it
(port/RULES.md, R23).

    .upstream/graphiti/.venv/bin/python port/tools/record_error_messages.py
    .upstream/graphiti/.venv/bin/python port/tools/record_error_messages.py --check

It writes tests/Port.Evaluation/values/errors.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream
in .upstream/graphiti. It changes nothing there. Nothing here uses the network.

Each case holds the class name, the arguments, str(exception), the message attribute, and the
names of the base classes in method resolution order up to Exception.
"""
from __future__ import annotations

import inspect
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'errors.json'

TEXTS = [
    'abc',
    '',
    'Zoë 東京',
    'a"b\'c',
    "it's",
    'line\nbreak',
    'tab\there',
    'emoji 😀',
    'é',
    '{braces} %s {0}',
]
LISTS = [
    [],
    ['a'],
    ['a', 'b'],
    ["it's", 'q"x'],
    ['Zoë', '東京'],
    ['both \' and "'],
    ['back\\slash', 'line\nbreak'],
    ['😀', 'é', ''],
]
INTS = [0, 7, -1, 2**63 - 1]


def values_for(name: str, annotation: object) -> list[object]:
    text = str(annotation)
    if 'list' in text:
        return LISTS
    if 'int' in text:
        return INTS
    if 'str' in text:
        return TEXTS
    raise SystemExit(f'no values for the parameter {name} with the annotation {text}')


def argument_sets(cls: type) -> list[list[object]]:
    """One set for each value of each parameter, with the first value for the other parameters."""
    if '__init__' not in vars(cls):
        return [[], ['abc'], ['Zoë 東京'], ['a', 'b']]
    parameters = list(inspect.signature(cls.__init__).parameters.values())[1:]
    columns = [values_for(p.name, p.annotation) for p in parameters]
    sets: list[list[object]] = []
    if any(p.default is not inspect.Parameter.empty for p in parameters):
        sets.append([])
    for index, column in enumerate(columns):
        for value in column:
            row = [c[0] for c in columns]
            row[index] = value
            if row not in sets:
                sets.append(row)
    return sets


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    import graphiti_core.errors as errors

    classes = [
        cls
        for _, cls in inspect.getmembers(errors, inspect.isclass)
        if cls.__module__ == errors.__name__
    ]
    classes.sort(key=lambda cls: inspect.getsourcelines(cls)[1])
    cases = []
    for cls in classes:
        bases = []
        for base in cls.__mro__[1:]:
            bases.append(base.__name__)
            if base is Exception:
                break
        for arguments in argument_sets(cls):
            error = cls(*arguments)
            cases.append(
                {
                    'class': cls.__name__,
                    'args': arguments,
                    'str': str(error),
                    'message': getattr(error, 'message', None),
                    'bases': bases,
                }
            )
    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/errors.py',
        'cases': cases,
    }
    text = json.dumps(document, ensure_ascii=True, indent=1) + '\n'
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(cases)} cases of {len(classes)} classes')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(cases)} cases of {len(classes)} classes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
