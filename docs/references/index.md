# References

| Reference | Use |
| --- | --- |
| [Upstream Graphiti](https://github.com/getzep/graphiti) at the commit in [port/UPSTREAM_PIN](../../port/UPSTREAM_PIN) | The source of the port. `scripts/fetch-upstream.sh` clones it into `.upstream/graphiti`. |
| Upstream `spec/` folder in that checkout | Upstream designs: configurable prompts, concurrent group ingestion, driver operations, LLM runtime, decision clients. |
| [Graphiti documentation](https://help.getzep.com/graphiti/graphiti/overview) | Concepts and user guides. |
| [How the Claude Code team thinks about verification](https://claude.dev/blog/how-the-claude-code-team-thinks-about-verification/) | The basis of [TESTING.md](../TESTING.md) and [REVIEW.md](../../REVIEW.md). |
| [Claude Code cloud environments](https://code.claude.com/docs/en/cloud-environments) | Setup scripts, SessionStart hooks, installed tools, and limits for cloud sessions. |
| [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/) | Mutation testing configuration and report format. |
| [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai) | The `IChatClient` and `IEmbeddingGenerator` contracts. |
| `port/tools/upstream-tests.sh` | Runs the upstream pytest suite in the configurations that upstream CI uses. |
| The `port-python-to-dotnet` skill | Port phases, gates, judge design, rulebook, and scaffold. |

The bootstrap profile also names reference files for a design system, Nixpacks, and uv. This repository has no design system and no Nixpacks setup.
Only the judge recorder uses uv, and plan 0002 documents that use.
