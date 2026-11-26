#!/usr/bin/env python3
"""Writes docs/generated/db-schema.md from the ported query definitions (plan 0004, after layer 5).

    python3 scripts/db_schema.py           write the file
    python3 scripts/db_schema.py --check   exit 1 if the file is not current

The script reads these C# files and uses only the Python 3 standard library:

- src/Graphiti.Net/Core/GraphQueries.cs: the range indexes and the full-text indexes of Neo4j.
- src/Graphiti.Net/Core/Models/Nodes/NodeDbQueries.cs: the save query of each node type.
- src/Graphiti.Net/Core/Models/Edges/EdgeDbQueries.cs: the save query of each edge type.
- src/Graphiti.Net/Core/Nodes.cs and Edges.cs: the keys of the maps that the model classes pass to a query
  that has `SET n = $entity_data` or `SET e = $edge_data`.

It reads the Neo4j form of each query, which is the `default` branch of the C# switch. A change to a label, a type,
a property, or an index in those files changes the output. The script does not run a query.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'docs' / 'generated' / 'db-schema.md'
CORE = ROOT / 'src' / 'Graphiti.Net' / 'Core'
GRAPH_QUERIES = CORE / 'GraphQueries.cs'
NODE_QUERIES = CORE / 'Models' / 'Nodes' / 'NodeDbQueries.cs'
EDGE_QUERIES = CORE / 'Models' / 'Edges' / 'EdgeDbQueries.cs'
NODES = CORE / 'Nodes.cs'
EDGES = CORE / 'Edges.cs'

# (title, C# name of the save query, the file that holds it)
NODE_SAVES = [
    ('Entity', 'GetEntityNodeSaveQuery', NODE_QUERIES),
    ('Episodic', 'GetEpisodeNodeSaveQuery', NODE_QUERIES),
    ('Community', 'GetCommunityNodeSaveQuery', NODE_QUERIES),
    ('Saga', 'GetSagaNodeSaveQuery', NODE_QUERIES),
]
EDGE_SAVES = [
    ('MENTIONS', 'EpisodicEdgeSave', EDGE_QUERIES),
    ('RELATES_TO', 'GetEntityEdgeSaveQuery', EDGE_QUERIES),
    ('HAS_MEMBER', 'GetCommunityEdgeSaveQuery', EDGE_QUERIES),
    ('HAS_EPISODE', 'HasEpisodeEdgeSave', EDGE_QUERIES),
    ('NEXT_EPISODE', 'NextEpisodeEdgeSave', EDGE_QUERIES),
]


# ---------- reading the C# text ----------

def read(path: Path) -> str:
    return path.read_text(encoding='utf-8')


def section(text: str, name: str) -> str:
    """The text of one member: from its declaration to the next member of the class that starts with `internal`."""
    match = re.search(rf'^    internal [^\n]*\b{re.escape(name)}\b', text, re.M)
    if not match:
        raise SystemExit(f'db_schema: {name} is not in the C# source')
    following = re.search(r'^    internal ', text[match.end():], re.M)
    end = match.end() + following.start() if following else len(text)
    return text[match.start():end]


def neo4j_branch(body: str) -> str:
    """The Neo4j form of a query. The C# switch ends with `default: // Neo4j`. A constant has no switch."""
    index = body.rfind('default:')
    return body[index:] if index != -1 else body


def block_after(text: str, marker: str) -> str:
    """The text of the brace block that follows the marker. The match counts braces."""
    start = text.index(marker)
    open_at = text.index('{', start)
    depth = 0
    for position in range(open_at, len(text)):
        if text[position] == '{':
            depth += 1
        elif text[position] == '}':
            depth -= 1
            if depth == 0:
                return text[open_at + 1:position]
    raise SystemExit(f'db_schema: no end for the block after {marker}')


def map_keys(path: Path, variable: str) -> list[str]:
    """The keys of `var <variable> = new OrderedDictionary<...> { ["key"] = ..., }` in a model class."""
    text = read(path)
    block = block_after(text, f'var {variable} = new OrderedDictionary<string, object?>')
    return re.findall(r'\["(\w+)"\]', block)


# ---------- reading one save query ----------

def analyse(query: str, caller_keys: dict[str, list[str]]) -> dict:
    info: dict = {'properties': [], 'vector': None, 'extra_labels': False, 'attributes': False, 'from': None, 'to': None}

    node = re.search(r'MERGE \((\w+):(\w+) \{uuid:', query)
    rel = re.search(r'MERGE \((\w+)\)-\[(\w+):(\w+) \{uuid:', query)
    if node:
        info['label'] = node.group(2)
        variable = node.group(1)
    elif rel:
        info['type'] = rel.group(3)
        variable = rel.group(2)
        aliases = {
            alias: [label for label in re.split(r'[|\s]+', labels) if label]
            for alias, labels in re.findall(r'MATCH \((\w+):([\w| ]+?) \{', query)
        }
        info['from'] = aliases.get(rel.group(1), [])
        target = re.search(r'\]->\((\w+)\)', query)
        info['to'] = aliases.get(target.group(1), []) if target else []
    else:
        raise SystemExit('db_schema: a save query has no MERGE of a node or a relationship')

    properties = ['uuid']

    literal = re.search(rf'SET {variable} = \{{(.*?)\}}', query, re.S)
    if literal:
        properties = re.findall(r'(\w+)\s*:\s*\$', literal.group(1))
    elif re.search(rf'SET {variable} = \$(\w+)', query):
        parameter = re.search(rf'SET {variable} = \$(\w+)', query).group(1)
        properties = list(caller_keys[parameter])
        info['attributes'] = True
    else:
        assigned = re.findall(rf'\b{variable}\.(\w+)\s*=\s*\$\w+', query)
        properties = ['uuid'] + [name for name in assigned if name != 'uuid']

    vector = re.search(r'set(?:Node|Relationship)VectorProperty\(\w+, "(\w+)"', query)
    if vector:
        info['vector'] = vector.group(1)
        if vector.group(1) not in properties:
            properties.append(vector.group(1))

    info['extra_labels'] = bool(re.search(rf'SET {variable}:\{{\{{?labels\}}\}}?', query))
    info['properties'] = properties
    return info


# ---------- reading the indexes ----------

def strip_falkordb(body: str) -> str:
    """Removes the `if (provider == GraphProvider.Falkordb) { ... }` block, so only the Neo4j list remains."""
    marker = 'if (provider == GraphProvider.Falkordb)'
    if marker not in body:
        return body
    start = body.index(marker)
    open_at = body.index('{', start)
    inner = block_after(body[start:], marker)
    return body[:start] + body[open_at + len(inner) + 2:]


def target_of(text: str) -> tuple[str, str]:
    node = re.search(r'\(\w+:(\w+)\)', text)
    rel = re.search(r'\[\w+:(\w+)\]', text)
    if rel:
        return 'relationship', rel.group(1)
    if node:
        return 'node', node.group(1)
    raise SystemExit(f'db_schema: no label or type in "{text}"')


def properties_of(text: str) -> list[str]:
    return [re.sub(r'^\w+\.', '', item.strip()) for item in text.split(',') if item.strip()]


def indexes() -> list[dict]:
    text = read(GRAPH_QUERIES)
    found = []
    range_body = strip_falkordb(section(text, 'GetRangeIndices'))
    for name, target, props in re.findall(r'"CREATE INDEX (\w+) IF NOT EXISTS FOR (\S+) ON \(([^)]*)\)"', range_body):
        owner, label = target_of(target)
        found.append({'name': name, 'kind': 'RANGE', 'owner': owner, 'label': label, 'properties': properties_of(props)})
    fulltext_body = strip_falkordb(section(text, 'GetFulltextIndices'))
    for name, target, props in re.findall(
            r'CREATE FULLTEXT INDEX (\w+) IF NOT EXISTS\s+FOR (\S+) ON EACH \[([^\]]*)\]', fulltext_body):
        owner, label = target_of(target)
        found.append({'name': name, 'kind': 'FULLTEXT', 'owner': owner, 'label': label, 'properties': properties_of(props)})
    if not found:
        raise SystemExit('db_schema: no index found')
    return found


# ---------- writing the page ----------

def code(items: list[str]) -> str:
    return ', '.join(f'`{item}`' for item in items)


def render() -> str:
    caller_keys = {
        'entity_data': map_keys(NODES, 'entityData'),
        'edge_data': map_keys(EDGES, 'edgeData'),
    }

    nodes = []
    for title, name, path in NODE_SAVES:
        info = analyse(neo4j_branch(section(read(path), name)), caller_keys)
        nodes.append((title, name, info))
    edges = []
    for title, name, path in EDGE_SAVES:
        info = analyse(neo4j_branch(section(read(path), name)), caller_keys)
        edges.append((title, name, info))
    index_list = indexes()

    lines = [
        '# Graph schema',
        '',
        'Status: generated. Do not edit this file by hand.',
        '',
        'Graphiti.Net has no relational database for the graph. The stored shapes are Neo4j node labels, relationship types, '
        'properties, and indexes.',
        'This page lists them as the Neo4j save queries of the port write them.',
        '',
        'The script `scripts/db_schema.py` reads these files:',
        '',
        '- `src/Graphiti.Net/Core/Models/Nodes/NodeDbQueries.cs` for the node save queries.',
        '- `src/Graphiti.Net/Core/Models/Edges/EdgeDbQueries.cs` for the edge save queries.',
        '- `src/Graphiti.Net/Core/GraphQueries.cs` for the indexes.',
        '- `src/Graphiti.Net/Core/Nodes.cs` and `src/Graphiti.Net/Core/Edges.cs` for the keys of the maps that a save query '
        'takes as one parameter.',
        '',
        'Run `python3 scripts/db_schema.py` to write this page again. Run `python3 scripts/db_schema.py --check` to find out '
        'whether it is current.',
        'The upstream definitions are in `graphiti_core/graph_queries.py`, `graphiti_core/models/nodes/node_db_queries.py`, '
        'and `graphiti_core/models/edges/edge_db_queries.py`.',
        'The port keeps the same queries. The other graph databases use other forms of these queries, and this page does not '
        'list them.',
        '',
        '## Node labels',
        '',
        f'The graph has {len(nodes)} node labels. Each save query merges the node on its `uuid`.',
        '',
        '| Label | Save query | Properties |',
        '| --- | --- | --- |',
    ]
    for title, name, info in nodes:
        notes = ''
        if info['attributes']:
            notes += ' Each key of the attribute dictionary of the node is also a property.'
        lines.append(f"| `{info['label']}` | `{name}` | {code(info['properties'])}.{notes} |")

    lines += ['', 'Details by label:', '']
    for title, name, info in nodes:
        lines.append(f"- `{info['label']}`: the query merges on `uuid`.")
        if info['vector']:
            lines.append(f"  The property `{info['vector']}` is a vector. The query sets it with a Neo4j procedure.")
        if info['extra_labels']:
            lines.append('  The node also gets each label that the caller gives. The caller adds `Entity` to the list.')

    lines += [
        '',
        '## Relationship types',
        '',
        f'The graph has {len(edges)} relationship types. Each save query merges the relationship on its `uuid`.',
        '',
        '| Type | From | To | Save query | Properties |',
        '| --- | --- | --- | --- | --- |',
    ]
    for title, name, info in edges:
        notes = ''
        if info['attributes']:
            notes += ' Each key of the attribute dictionary of the edge is also a property.'
        source = ' or '.join(f'`{label}`' for label in info['from'])
        target = ' or '.join(f'`{label}`' for label in info['to'])
        lines.append(f"| `{info['type']}` | {source} | {target} | `{name}` | {code(info['properties'])}.{notes} |")

    lines += ['']
    for title, name, info in edges:
        if info['vector']:
            lines.append(f"- `{info['type']}`: the property `{info['vector']}` is a vector. The query sets it with a Neo4j procedure.")
    if lines[-1] != '':
        lines.append('')

    lines += [
        '## Indexes',
        '',
        'The method `BuildIndicesAndConstraintsAsync` of the Neo4j driver runs one `CREATE ... IF NOT EXISTS` statement for '
        'each index below. A second call changes nothing.',
        f'There are {sum(1 for i in index_list if i["kind"] == "RANGE")} range indexes and '
        f'{sum(1 for i in index_list if i["kind"] == "FULLTEXT")} full-text indexes.',
        '',
        '| Index | Kind | On | Properties |',
        '| --- | --- | --- | --- |',
    ]
    for item in index_list:
        owner = 'label' if item['owner'] == 'node' else 'relationship type'
        lines.append(f"| `{item['name']}` | {item['kind']} | {owner} `{item['label']}` | {code(item['properties'])} |")

    lines += [
        '',
        '## Ingestion state',
        '',
        'The ingestion state store uses SQLite. Its tables are defined upstream in '
        '`graphiti_core/ingestion/sqlite_state_store.py`.',
        '',
    ]
    return '\n'.join(lines)


def main() -> int:
    text = render()
    if '--check' in sys.argv[1:]:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else None
        if current != text:
            print(f'db_schema: {OUT.relative_to(ROOT)} is not current. Run python3 scripts/db_schema.py.', file=sys.stderr)
            return 1
        print(f'db_schema: {OUT.relative_to(ROOT)} is current')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'db_schema: wrote {OUT.relative_to(ROOT)}')
    return 0


if __name__ == '__main__':
    sys.exit(main())
