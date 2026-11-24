# Security

## Trust boundaries

| Boundary | Risk | Rule |
| --- | --- | --- |
| Episode content to LLM prompts | Episode text is untrusted input and can carry instructions. | The port keeps the upstream prompts unchanged. Extraction output is data, never a command. |
| Names to Cypher | A label or property name from user input can inject Cypher. | Reject unsafe names before they enter a query string. Upstream covers this in `tests/test_node_label_security.py`, and the port must match it. |
| Group ids | A query without a `group_id` filter reads another tenant's data. | Reviewers check every changed query. See [REVIEW.md](../REVIEW.md). |
| Logs and traces | Episode text, prompts, and credentials can leak. | No episode content, prompt, or credential in logs or span attributes by default. |
| Upstream checkout | `.upstream/` is third-party code. | Read it. Only the judge recorder runs it, inside its own `uv` environment. Never edit it. |

## Credentials

- The repository holds no real secret. `.env` is git-ignored.
- `NEO4J_PASSWORD=password` in `.env.example`, `docker-compose.yml`, and the test fixture is a local test value. The sidecar binds to `127.0.0.1` only.
- The library never reads `.env` files. The host application supplies connection values and model clients.
- The M1 quickstart run needs a real LLM key. Pass it as an environment variable. In a Claude cloud environment, store it as a network secret.
- NuGet publishing needs an API key that only the maintainer holds. An agent never publishes without approval.

## Telemetry

Graphiti.Net sends no usage telemetry. Upstream's PostHog client is not ported (decision D9 in [port/DECISIONS.md](../port/DECISIONS.md)).

## Agent permissions

`.claude/settings.json` allows the build, test, and environment commands. It denies edits to `.upstream/`. From gate 2 it also denies edits to `port/judge/`.

## Reports

No security policy file exists yet. Before the first public release, add `SECURITY.md` at the root with a contact.
