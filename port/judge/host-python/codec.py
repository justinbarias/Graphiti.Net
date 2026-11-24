"""Value codec for the judge protocol.

Cases carry arguments as JSON with a few tags. Results go back as JSON with the same tags.
The .NET host implements the same tags. README.md lists them.
"""
from __future__ import annotations

import dataclasses
import enum
import importlib
import json
import math
from datetime import date, datetime
from typing import Any


def resolve(path: str) -> Any:
    """Resolves 'package.module:attr.attr' to a Python object."""
    module_name, _, attrs = path.partition(':')
    obj: Any = importlib.import_module(module_name)
    for attr in filter(None, attrs.split('.')):
        obj = getattr(obj, attr)
    return obj


def decode(value: Any) -> Any:
    if isinstance(value, list):
        return [decode(v) for v in value]
    if not isinstance(value, dict):
        return value
    if '$dt' in value:
        return datetime.fromisoformat(value['$dt'].replace('Z', '+00:00'))
    if '$ref' in value:
        return resolve(value['$ref'])
    if '$enum' in value:
        return resolve(value['$enum'])(value['value'])
    if '$model' in value:
        return resolve(value['$model'])(**decode(value.get('data', {})))
    if '$tuple' in value:
        return tuple(decode(v) for v in value['$tuple'])
    if '$set' in value:
        return {decode(v) for v in value['$set']}
    if '$bytes' in value:
        return bytes.fromhex(value['$bytes'])
    if '$float' in value:
        return float(value['$float'])
    if '$dict' in value:
        return {decode(k): decode(v) for k, v in value['$dict']}
    return {k: decode(v) for k, v in value.items()}


def encode(value: Any) -> Any:
    if value is None or isinstance(value, (bool, int, str)):
        return value
    if isinstance(value, float):
        if math.isnan(value) or math.isinf(value):
            return {'$float': repr(value)}
        return value
    if isinstance(value, enum.Enum):
        return encode(value.value)
    if isinstance(value, datetime):
        return {'$dt': value.isoformat()}
    if isinstance(value, date):
        return {'$date': value.isoformat()}
    if isinstance(value, bytes):
        return {'$bytes': value.hex()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    if isinstance(value, (set, frozenset)):
        items = [encode(v) for v in value]
        return {'$set': sorted(items, key=lambda v: json.dumps(v, sort_keys=True))}
    if isinstance(value, dict):
        if all(isinstance(k, str) for k in value):
            return {k: encode(v) for k, v in value.items()}
        pairs = [[encode(k), encode(v)] for k, v in value.items()]
        return {'$dict': sorted(pairs, key=lambda p: json.dumps(p[0], sort_keys=True))}
    if isinstance(value, type):
        return {'$class': value.__name__}
    if isinstance(value, BaseException):
        return {'$exc': type(value).__name__}

    # pydantic models, without an import of pydantic in this module
    if hasattr(value, 'model_fields') and hasattr(value, 'model_dump'):
        fields = {name: encode(getattr(value, name)) for name in type(value).model_fields}
        extra = getattr(value, 'model_extra', None) or {}
        fields.update({k: encode(v) for k, v in extra.items()})
        return {'$type': type(value).__name__, **fields}
    if dataclasses.is_dataclass(value):
        fields = {f.name: encode(getattr(value, f.name)) for f in dataclasses.fields(value)}
        return {'$type': type(value).__name__, **fields}

    # neo4j temporal and graph values
    if hasattr(value, 'to_native'):
        return encode(value.to_native())
    if hasattr(value, 'tolist'):  # numpy arrays and scalars
        return encode(value.tolist())
    if hasattr(value, 'items') and hasattr(value, 'keys'):  # neo4j Record and Node
        return {str(k): encode(v) for k, v in value.items()}

    return {'$unknown': type(value).__name__}
