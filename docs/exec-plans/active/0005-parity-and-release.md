# 0005 Parity, cutover, and release

Port phases 7 and 8. Starts after plan 0004. Ends with M1.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Fix build errors in groups that share a cause. | Fixer, Sonnet 5.5, medium | `scripts/check.sh` passes. |
| 2 | Run the whole judge. Work the failing list down to zero. | Lead, Fable 5.2, medium, with Sonnet 5.5 fixers | `expected-failures.txt` is empty. The case count did not go down. |
| 3 | Copy the skill's `scan_port.py` into `port/tools/`. Run it on `src/` with `--exclude /obj/ --exclude /bin/`. Add it and the judge to CI as merge checks. | Lead, Fable 5.2, medium | No finding. |
| 4 | Write the C# quickstart in `examples/Quickstart`. Run it against Neo4j with a real LLM. The quickstart is interactive (see below). | Workhorse, Sonnet 5.5, medium | The run output, and the text of one chat session. The maintainer supplies the key. |
| 5 | Close debt T1, T2, and T3: run the gate in the devcontainer, in a cloud session, and in CI. | Lead, Fable 5.2, medium | The output from each environment. |
| 6 | Run `scripts/mutate.sh`. Record the baseline score for M3. | Lead, Fable 5.2, medium | The score, in QUALITY_SCORE.md. |
| 7 | Package: README for NuGet, package icon, symbols, SourceLink, the NOTICE file inside each package. Add a root security policy. | Workhorse, Sonnet 5.5, medium | `dotnet pack -c Release` produces both packages. A scratch project installs them from a local feed and runs the quickstart. |
| 8 | Integrated adversarial pass over the full diff. | Reviewer of the maintainer's choice | No open `important` finding. |
| 9 | Build the typed entity types and edge types: the section "Typed entity types and edge types" of [the public surface design](../../design-docs/public-surface-design.md). Start after the branch `port/public-surface` is on `main`. Finish before tasks 4 and 7, so that the quickstart and the package README show the typed form. First the lead gives decisions A1 to A13 a number in `port/DECISIONS.md` and adds the class name to `GraphTypeDefinition` (decision A4). Answer open points P1 to P3 of the section with a test before the reader depends on them. Use a stack of small pull requests: the builder change, the reader with the attributes, the ontology with the registration, and the read path. | Workhorse, Opus 5.5, medium. The lead reviews each layer. One adversarial review for each layer before its commit. | `scripts/check.sh --test` passes, and the judge result does not change. Typed classes for the five judge fixtures give the same names, descriptions, fields, and schema text as the fixture descriptors of the judge host. The edge names are `LIKES` and `WORKS_AT`. One end-to-end test with the scripted chat client makes two calls, one with the typed ontology and one with the lower-level lists. Both send the same model requests. One end-to-end test reads a result with `Is<T>()` and `As<T>()`. One test exists for each row of the error table. |

## The interactive quickstart (task 4)

The maintainer asked for this form on 2026-11-28:

- The example ingests one piece of legislation of 150 to 200 pages into the graph.
- An agent of Microsoft Agent Framework answers questions. The graph search of Graphiti.Net is a tool of the agent.
- An AG-UI HTTP server exposes the agent.
- A CopilotKit page is the chat front end, and it talks to the server over AG-UI.
- The example uses the OpenAI model `gpt-6-luna`. The maintainer first named `gpt-6.1-luna`. The account has no model with that name, and the maintainer chose `gpt-6-luna` on 2026-11-28. The model name is a setting of the example.

These libraries are dependencies of the example only. The two packages of the product do not reference them.

## Gate 4

The maintainer sees the full judge pass and the scan result, and accepts or rejects each remaining `TODO(port)` marker by name.

## Release

Publishing `Graphiti.Net` and `Graphiti.Net.Neo4j` version `0.1.0-preview.1` to nuget.org needs the maintainer's explicit approval and the maintainer's API key.
Keep the Python oracle runnable and the judge able to compare both builds for as long as the maintainer wants a way back.

## Results

### Task 1, the build, 2026-11-28

No group of build errors exists. Each layer of plan 0004 merged with a green gate, so `scripts/check.sh` passes on `main` with 0 warnings and 0 errors.

### Task 2, the listed judge cases, 2026-11-28

After layer 9, `expected-failures.txt` held 66 pure ids and no flow id. A reader classified each one, and a Sonnet 5.5 fixer at high effort closed the 22 ids that need no decision of the maintainer. 44 ids stay listed.

| Group | Ids | What closed it |
| --- | --- | --- |
| A tuple for an `Iterable[int]` parameter | 1 | The judge host lets a parameter of the exact type `IEnumerable<T>` take a tuple. This change has its own pull request. |
| `None` in a list of group ids | 1 | `validate_group_ids` takes an `IEnumerable<string?>?`. Python declares `str | None` for the item. |
| A class and a model that the host did not know | 2 | The host registers `datetime:date` and `neo4j.time:DateTime`. |
| The schema of three result records | 3 | `AddEpisodeResults`, `AddBulkEpisodeResults`, and `AddTripletResults` have a `ModelJsonSchema` member. |
| A label list for `get_entity_node_save_query` | 3 | The parameter is an `object?`, as its helper declares `str | list[str]`. |
| A fraction for `rank_const` of `rrf` | 1 | The parameter is a `double`. Python gives it no annotation. |
| `ExtractedEntities` as a custom type | 10 | The host gives the class a descriptor that its binding file writes by hand. |
| A dict as a prompt library | 1 | The host decodes a dict as a library with no group. |

Each of the six seeded defects of the fixer fails a judge case.

Decisions of the lead under D49, for the maintainer at gate 4:

- A parameter of the exact type `IEnumerable<T>` takes a tuple in the judge host. A `List<T>` does not (rule R22).
- `rank_const` of `rrf` is a `double`. `port/TYPE_HINTS.md` gave `int` before.
- A parameter that goes unchanged to a helper with a wider declared type takes the type of the helper. `GetEntityNodeSaveQuery` takes an `object?` for `labels`.

The 44 ids that stay are the cases of open item O4 in `port/RULES.md`. In each one, Python gets its result by accident: a value of a wrong type passes a falsy test, falls into a default branch, or is read one character at a time. The C# type cannot hold the value, and each real caller passes the right type. The rows of O4 name each id with the proposed answer. The maintainer accepts each one by name at gate 4, or extends decision D20.

| Area | Ids that stay |
| --- | --- |
| `chunking` | 5 |
| `dedupe` | 4 |
| `filters` | 2 |
| `prompts` | 4 |
| `queries` | 10 |
| `ranking` | 3 |
| `text` | 16 |

The evidence of task 2 in the plan table says that `expected-failures.txt` is empty. It is not empty, and it holds these 44 ids until the maintainer decides at gate 4.
