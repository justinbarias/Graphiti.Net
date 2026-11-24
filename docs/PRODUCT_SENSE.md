# Product sense

The requirements live in [product-specs/spec.md](product-specs/spec.md), and [product-specs/index.md](product-specs/index.md) lists every product document. This page records the intent behind them.

- The user wants Graphiti's behavior in .NET. A developer who knows the Python library must find the same concepts, the same search recipes, and the same graph in the database.
- A graph that the Python build wrote must stay readable by Graphiti.Net, and the reverse. Teams run both.
- The library is a dependency inside someone else's agent. It must not read `.env` files, send telemetry, or pick a model provider for the user.
- The first-use path is short: start Neo4j, register Graphiti with a chat client and an embedding generator, add an episode, search. See [product-specs/new-user-onboarding.md](product-specs/new-user-onboarding.md).
- A feature that upstream does not have needs a strong reason, because no oracle can judge it. Azure Cosmos DB Gremlin is the one planned case, and it starts with a spike.
