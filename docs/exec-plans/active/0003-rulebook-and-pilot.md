# 0003 Rulebook, PyCompat, and pilot

Port phases 4 and 5. Starts after gate 2. Ends at gate 3.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Copy the skill's `references/rules.md` to `port/RULES.md`. Add rules for this repository: naming, file layout, error types, logging, pydantic models and validators, async and cancellation, Cypher strings, prompts, and each answer in `port/GAPS.md`. | Lead, Fable 5.2, high | Every gap in `GAPS.md` maps to a rule. |
| 2 | Write the public surface design document: `AddGraphiti`, options, the `Microsoft.Extensions.AI` adapter, the cross-encoder over `IChatClient`. This is an ask-first item. | Lead, Fable 5.2, high | The maintainer approves it. |
| 3 | Create `src/Graphiti.Net.PyCompat` and `tests/Graphiti.Net.Property.Tests`. Build only the helpers that the queue needs. Generate expected values from Python into a file. | Workhorse, Sonnet 5.5, medium | CsCheck property tests pass against the Python values, including negative numbers, zero divisors, empty strings, an emoji, and a combining mark. |
| 4 | Write `scripts/mutation_survivors.py`. It reads the Stryker JSON report and lists the tests that the pruning rule in TESTING.md deletes. | Workhorse, Sonnet 5.5, medium | Run on the PyCompat report. Closes debt T4. |
| 5 | Run `scripts/mutate.sh` on PyCompat. Apply the pruning rule. | Lead, Fable 5.2, medium | The score and the list of deleted tests, in this plan. |
| 6 | Pilot. Port five units of different kinds using only `port/RULES.md`: `errors` (144 lines), `search.search_filters` (cycle group), `prompts.dedupe_nodes`, `driver.neo4j.operations.entity_node_ops`, `utils.content_chunking` (826 lines). | Implementers, Sonnet 5.5, medium. Opus 5.5 medium for the cycle group. | Build, format, and the covering judge cases pass for each unit. |
| 7 | Review the pilot. Two fresh reviewers for each unit. If they disagree, a third reviewer decides. | Reviewers, Opus 5.5, high | Findings with inputs. Each finding that shows a missing rule changes `RULES.md`. |
| 8 | Decide what to do with the pilot output. If a rule changed in a way that the output does not follow, delete the output. | Lead, Fable 5.2, medium | The decision, in `port/STATE.md`. |

## Gate 3

The maintainer approves `port/RULES.md`.

## Results

None yet.
