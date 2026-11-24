# New user onboarding

Status: planned for M1. No step below works yet, because the product projects contain no ported code.

The target is the upstream quickstart (`examples/quickstart/quickstart_neo4j.py`), written in C#.

## Target path

1. Start Neo4j.

   ```bash
   docker compose up -d --wait neo4j
   ```

2. Add the `Graphiti.Net` and `Graphiti.Net.Neo4j` packages to an application.
3. Register an `IChatClient` and an `IEmbeddingGenerator` from any provider package.
4. Register Graphiti and the Neo4j driver with the connection values.
5. Build the indexes and constraints one time.
6. Add an episode.
7. Search, and read the facts with their validity windows.

## Acceptance

- A new user completes the path from an empty folder with only the README.
- The C# quickstart runs against a real LLM. This is done criterion 4 of M1 in the [spec](spec.md).
- The API names in the steps above are fixed in plan 0003, in the public surface design document.
