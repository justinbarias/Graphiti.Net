# Graph schema

Status: not generated yet.

Graphiti.Net has no relational database. The stored shapes are Neo4j node labels, relationship types, properties, and indexes.
Upstream defines them in `graphiti_core/graph_queries.py`, `graphiti_core/models/nodes/node_db_queries.py`, and `graphiti_core/models/edges/edge_db_queries.py`.

Plan 0004 adds a script that writes this page from the ported query definitions.
Until then, read the upstream files in `.upstream/graphiti` after you run `scripts/fetch-upstream.sh`.

The ingestion state store uses SQLite. Its tables are defined upstream in `graphiti_core/ingestion/sqlite_state_store.py`.
