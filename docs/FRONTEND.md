# User-facing surfaces

Graphiti.Net has no graphical interface, no web frontend, and no design system.

The surfaces that a user touches:

| Surface | State |
| --- | --- |
| The public API of the `Graphiti.Net` and `Graphiti.Net.Neo4j` packages | Planned for M1. Choice 4 under "Lead choices accepted at gate 1" in [port/DECISIONS.md](../port/DECISIONS.md) describes its style. |
| The MCP server tools | Planned for M2. |
| The REST service | Planned for M4. |

To inspect a graph by eye, open the Neo4j browser at `http://localhost:7474` while the sidecar runs.
