# Core beliefs

Status: approved in the bootstrap interview on 2026-11-24.

1. Python is the oracle. Before M1, a behavior difference from upstream is a bug in the port. This holds even for upstream behavior that looks wrong. Report upstream bugs. Do not fix them in the port.
2. Evidence over reading. A claim needs command output.
3. Structure first, idiom later. A one-to-one port is easy to review against the source. Idiomatic changes come one at a time after M1, with the judge run after each change.
4. Few tests, and each one must catch real bugs. End-to-end, differential, and property tests earn their place. Mutation testing removes the rest.
5. Real dependencies in tests. A real Neo4j, and fakes only at the LLM network boundary.
6. One setup path. A local machine, the devcontainer, a cloud session, and CI run the same scripts.
7. Checks stay out of reach of the changes they judge.
8. No usage telemetry.
9. Rules over patches. If a mistake shows up twice, fix the rulebook and redo the affected units.
