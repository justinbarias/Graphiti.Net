"""Records the names and the defaults of the upstream surface that the public .NET surface maps (plan 0004, layer 9, P9d).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_public_surface.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_public_surface.py --check

The tool imports graphiti_core at the pinned commit and records:

- each public method of Graphiti, and Graphiti.__init__, with each parameter name and default (inspect.signature),
  and the names of the public properties of Graphiti
- each public method of the 9 namespace classes of namespaces/nodes.py and namespaces/edges.py
- the parameters of LLMConfig.__init__
- each field of the node, edge, search config, search filter, and result models, of RawEpisode, and of
  ConcurrentIngestionConfig, with its default (the pydantic field list, in its order)
- each field of each of the 16 recipes of search_config_recipes.py (model_dump, with each enum as its member name)
- one ConcurrentIngestionConfig with a value that differs from the default in each number field

A default is {"required": true}, {"value": <value>}, or {"factory": "uuid4"} and {"factory": "utc_now"} for the
factories whose value differs at each call. An enum value is {"enum": <class>, "member": <name>}. A recipe value is
{"recipe": <name>}.

It writes tests/Graphiti.Net.E2E.Tests/values/public_surface.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the upstream folder
from GRAPHITI_UPSTREAM, then from .upstream/graphiti in the repository, then from the main checkout when this is a git
worktree. It opens no network connection.
"""
from __future__ import annotations

import inspect
import json
import os
import sys
import uuid as uuid_module
from datetime import datetime
from enum import Enum
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'tests' / 'Graphiti.Net.E2E.Tests' / 'values' / 'public_surface.json'


def find_upstream() -> Path:
    given = os.environ.get('GRAPHITI_UPSTREAM')
    if given:
        return Path(given)
    local = ROOT / '.upstream' / 'graphiti'
    if local.exists():
        return local
    # A git worktree has no .upstream of its own. The main checkout holds it.
    return ROOT.parents[2] / '.upstream' / 'graphiti'


sys.path.insert(0, str(find_upstream()))

from pydantic import BaseModel  # noqa: E402
from pydantic_core import PydanticUndefined  # noqa: E402

from graphiti_core import edges, graphiti, nodes  # noqa: E402
from graphiti_core.ingestion.config import ConcurrentIngestionConfig  # noqa: E402
from graphiti_core.llm_client.config import LLMConfig  # noqa: E402
from graphiti_core.namespaces import edges as edge_namespaces  # noqa: E402
from graphiti_core.namespaces import nodes as node_namespaces  # noqa: E402
from graphiti_core.search import search_config, search_config_recipes, search_filters  # noqa: E402
from graphiti_core.utils.bulk_utils import RawEpisode  # noqa: E402

RECIPES = {
    name: value
    for name, value in vars(search_config_recipes).items()
    if name.isupper() and isinstance(value, search_config.SearchConfig)
}

NODE_NAMESPACES = ['EntityNodeNamespace', 'EpisodeNodeNamespace', 'CommunityNodeNamespace', 'SagaNodeNamespace']
EDGE_NAMESPACES = ['EntityEdgeNamespace', 'EpisodicEdgeNamespace', 'CommunityEdgeNamespace',
                   'HasEpisodeEdgeNamespace', 'NextEpisodeEdgeNamespace']

MODELS = [
    nodes.Node, nodes.EpisodicNode, nodes.EntityNode, nodes.CommunityNode, nodes.SagaNode,
    edges.Edge, edges.EpisodicEdge, edges.EntityEdge, edges.CommunityEdge, edges.HasEpisodeEdge, edges.NextEpisodeEdge,
    search_config.SearchConfig, search_config.EdgeSearchConfig, search_config.NodeSearchConfig,
    search_config.EpisodeSearchConfig, search_config.CommunitySearchConfig, search_config.SearchResults,
    search_filters.SearchFilters, search_filters.DateFilter, search_filters.PropertyFilter,
    graphiti.AddEpisodeResults, graphiti.AddBulkEpisodeResults, graphiti.AddTripletResults,
    RawEpisode, ConcurrentIngestionConfig,
]


def render_value(value):
    for name, recipe in RECIPES.items():
        if value is recipe:
            return {'recipe': name}
    if isinstance(value, Enum):
        return {'enum': type(value).__name__, 'member': value.name}
    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    if isinstance(value, (list, tuple)):
        return [render_value(item) for item in value]
    if isinstance(value, dict):
        return {str(key): render_value(item) for key, item in value.items()}
    if isinstance(value, BaseModel):
        return {'model': type(value).__name__, 'fields': render_value(value.model_dump())}
    raise SystemExit(f'no rendering for the default {value!r}')


def render_factory(factory):
    first, second = factory(), factory()
    if isinstance(first, datetime):
        return {'factory': 'utc_now'}
    if isinstance(first, str) and first != second:
        uuid_module.UUID(first)
        return {'factory': 'uuid4'}
    return {'value': render_value(first)}


def signature(function) -> dict:
    parameters = []
    for parameter in inspect.signature(function).parameters.values():
        if parameter.name == 'self':
            continue
        if parameter.default is inspect.Parameter.empty:
            default = {'required': True}
        else:
            default = {'value': render_value(parameter.default)}
        parameters.append({'name': parameter.name, 'default': default})
    return {'async': inspect.iscoroutinefunction(function), 'parameters': parameters}


def methods(cls) -> dict:
    return {
        name: signature(member)
        for name, member in cls.__dict__.items()
        if inspect.isfunction(member) and not name.startswith('_')
    }


def fields(model) -> list:
    out = []
    for name, field in model.model_fields.items():
        if field.default_factory is not None:
            default = render_factory(field.default_factory)
        elif field.default is PydanticUndefined:
            default = {'required': True}
        else:
            default = {'value': render_value(field.default)}
        out.append({'name': name, 'default': default})
    return out


def record() -> dict:
    graphiti_methods = methods(graphiti.Graphiti)
    graphiti_methods = {'__init__': signature(graphiti.Graphiti.__init__), **graphiti_methods}
    example = ConcurrentIngestionConfig(
        window=3,
        lease_seconds=90.0,
        write_margin_seconds=7.0,
        poll_interval_seconds=0.5,
        index_lag_seconds=11.0,
        max_apply_attempts=4,
    )
    return {
        'graphiti': graphiti_methods,
        'graphiti_properties': [
            name for name, member in graphiti.Graphiti.__dict__.items()
            if isinstance(member, property) and not name.startswith('_')
        ],
        'namespaces': {
            **{name: methods(getattr(node_namespaces, name)) for name in NODE_NAMESPACES},
            **{name: methods(getattr(edge_namespaces, name)) for name in EDGE_NAMESPACES},
        },
        'llm_config': signature(LLMConfig.__init__)['parameters'],
        'models': {model.__name__: fields(model) for model in MODELS},
        'recipes': {name: render_value(recipe.model_dump()) for name, recipe in RECIPES.items()},
        'ingestion_example': {
            key: value
            for key, value in example.model_dump().items()
            if isinstance(value, (int, float)) and not isinstance(value, bool)
        },
    }


def render(data) -> str:
    return json.dumps(data, ensure_ascii=False, indent=1) + '\n'


def main() -> int:
    text_out = render(record())
    if '--check' in sys.argv[1:]:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else None
        if current != text_out:
            print(f'{OUT.relative_to(ROOT)} is not current. Run the tool without --check.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)} is current')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text_out, encoding='utf-8')
    print(f'wrote {OUT.relative_to(ROOT)} ({len(text_out)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
