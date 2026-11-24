# Design

Navigation into design decisions.

| Topic | Home |
| --- | --- |
| Engineering principles | [design-docs/core-beliefs.md](design-docs/core-beliefs.md) |
| Design documents | [design-docs/index.md](design-docs/index.md) |
| Port scope and library choices | [../port/DECISIONS.md](../port/DECISIONS.md) |
| Implemented layout and boundaries | [../ARCHITECTURE.md](../ARCHITECTURE.md) |
| Stored graph shapes | [generated/db-schema.md](generated/db-schema.md) |

Before M1, the design of internal modules is the upstream design. A design document is needed only for the parts that upstream does not have:
the public .NET surface, the `Microsoft.Extensions.AI` adapter, and the judge.
