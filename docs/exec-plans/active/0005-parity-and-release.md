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

After layer 9, `expected-failures.txt` held 66 pure ids and no flow id. A reader classified each one, and a Sonnet 5.5 fixer at high effort closed 21 ids that need no decision of the maintainer. 45 ids stay listed.

| Group | Ids | What closed it |
| --- | --- | --- |
| A tuple for an `Iterable[int]` parameter | 1 | The judge host lets a parameter of the exact type `IEnumerable<T>` take a tuple. This change has its own pull request. |
| `None` in a list of group ids | 1 | `validate_group_ids` takes an `IEnumerable<string?>?`. Python passes each item to `validate_group_id`, which declares `str | None`. |
| A class and a model that the host did not know | 2 | The host registers `datetime:date` as a class. For `neo4j.time:DateTime` it builds a `LocalDateTime` and passes it through `Neo4jDriver.FromDriverValue` of the product. |
| The schema of three result records | 3 | `AddEpisodeResults`, `AddBulkEpisodeResults`, and `AddTripletResults` have a `ModelJsonSchema` member. |
| A label list for `get_entity_node_save_query` | 3 | The parameter is an `object?`, as its helper declares `str | list[str]`. |
| `ExtractedEntities` as a custom type | 10 | The host gives the class a descriptor that its binding file writes by hand. |
| A dict as a prompt library | 1 | The host decodes a value that is not a namespace as a library with no group. |

Each seeded defect of the fixer fails a judge case.

One Opus 5.5 high reviewer then read both pull requests with the task to refute them: 13 findings, 3 of them important. Each one is resolved:

- The neo4j date case passed because the host did the conversion. The host now runs the conversion of the product, and a seeded defect in it fails the case.
- A `double` for `rank_const` of `rrf` gave another result than Python for a whole number of 2 to the power of 53 or more. The change is taken back, and `pure.ranking.rrf.rank_const_fraction` stays listed.
- The lead had edited `port/TYPE_HINTS.md` by hand, which is a generated file. The edit is undone.
- `IEnumerable<string?>?` for `validate_group_ids` is an exception of the list rule. The rulebook names it now.
- Nine sentences of the records were false or stale. Each one is corrected.

Decisions of the lead under D49, for the maintainer at gate 4:

- A parameter of the exact type `IEnumerable<T>` takes a tuple in the judge host. A `List<T>` does not (rule R22).
- `validate_group_ids` takes an `IEnumerable<string?>?`, where the rule for a Python list gives a `List<T>`. A `List<string?>` parameter refuses the `List<string>` of each caller.
- A parameter that goes unchanged to a helper with a wider declared type takes the type of the helper. `GetEntityNodeSaveQuery` takes an `object?` for `labels`.

The 45 ids that stay are the cases of open item O4 in `port/RULES.md`. In each one, the C# type cannot hold the value, and each real caller passes the right type. In most of them, Python gets its result by accident: a value of a wrong type passes a falsy test, falls into a default branch, or is read one character at a time. In eight, Python acts on purpose: three cases of `create_prompt_library` and one of `LLMModel` raise `ValueError` for a name before they read the value, `model_copy` stores a value with no check, and three cases raise for the shape of the value. The rows of O4 name each id with the proposed answer. The maintainer accepts each one by name at gate 4, or extends decision D20.

| Area | Ids that stay |
| --- | --- |
| `chunking` | 5 |
| `dedupe` | 4 |
| `filters` | 2 |
| `prompts` | 4 |
| `queries` | 10 |
| `ranking` | 4 |
| `text` | 16 |

The evidence of task 2 in the plan table says that `expected-failures.txt` is empty. It is not empty, and it holds these 45 ids until the maintainer decides at gate 4.

### Task 3, the scanner and the merge checks, 2026-11-28

`port/tools/scan_port.py` started as a copy of the scanner of the port skill. `scripts/check.sh` runs it on `src/` after the format step, so each local gate and the CI job `check` run it. It keeps the six checks and the options of the skill's scanner, and it differs in three ways:

- It has two more options: `--allow FILE` and `--write-allow`.
- It reads each file with its state: a block comment, a raw string of more than one line, the depth of the braces, and the type and the member that hold a line. The skill's scanner reads each line alone.
- It finds the null-forgiving `!` operator after a name, a closing parenthesis, or a closing square bracket, when no `=` follows, and inside the holes of an interpolated string. The skill's scanner finds it only before five characters.

| Finding of the scanner on `src/` | Lines | State |
| --- | --- | --- |
| `stub` | 186 | In the allow file. 115 lines throw `NotImplementedException` where upstream raises `NotImplementedError`. 71 lines catch it where upstream catches `NotImplementedError`. |
| `port TODO` | 14 | In the allow file. 7 `TODO(port)` markers of 2 lines each. `port/STATE.md` lists them under "Blocked units". |
| `null-forgiving operator` | 0 | The first run of the wider pattern found one use in `Graphiti.cs`. Pull request 60 removed it. |
| `dynamic type`, `warning switched off`, `calls Python` | 0 | Nothing to accept, and the allow file cannot hold these labels. |

The evidence of task 3 in the plan table is "No finding". That is not the state: with no allow file the scanner reports 200 findings. The gate passes because `port/tools/scan_port_allow.txt` names each of them.

The allow file is a decision of the lead (D54), and it is not the acceptance of the maintainer. Item 5 of "Decisions for the maintainer" in `port/STATE.md` says that the maintainer accepts the stub sites by name at gate 4, and the `TODO(port)` markers wait for the same gate. The allow file is that list of names: 200 lines, one for each finding. Each line has a path, a label, a count, the member that holds the line, and the text of the line. The member is the name of the type and the first line of the declaration. Until gate 4 the file keeps the gate green and stops a new shortcut.

What fails the scan: a finding that the file does not name, a count that differs, a line of the file that the scan does not find, two lines of the file for one key, and a line of the file with a label other than `stub` and `port TODO`.

Seeds, each one undone, each with exit code 1:

- A `dynamic` field, a `!` operator, a new `TODO(port)` line, and a `#pragma warning disable` in `Helpers.cs`.
- One stub replaced by another exception, with a new stub in a new member: a method of `Nodes.cs`, a method with a tuple in its return type, and a property.
- A `throw new NotImplementedException()` added to a `catch` line that the file names.
- A `catch (NotImplementedException)` moved from `SaveAsync` of one class to a member of another class in `Edges.cs`.
- A `TODO(port)` comment moved to the end of `SearchUtils.cs`.
- The operator in the hole of an interpolated string, after a space before a dot, and after a string that holds `//`.

The words `Done!` in a string, in a line comment, and in a block comment gave no finding.

Limits of the scan, which a reviewer still covers:

- The scanner reads text and not syntax. It finds the member of a line from the braces and from the first line of each declaration.
- It does not see a line that moves inside its member, or a new line with the same text as a line that leaves the same member.
- It does not see the operator after a space, unless a dot and a name follow. It reads a verbatim string of more than one line as code.
- It does not see `<NoWarn>` in a project file, an exception of another type that stands for unported code, or an empty method body.
- It scans `src/` only. The plan names that folder. The example and the test projects are not scanned.

The CI job `check` ran the judge for each pull request before this task, because `scripts/check.sh --test` runs it. Two facts about the judge in the gate:

- The judge call of `scripts/check.sh` now has `--verbose`. A case that fails and is not listed, or that is listed and bound and wrong, then prints 20 difference lines, where it printed 3 (debt T32).
- The count of judge cases cannot go down: the judge fails if it runs fewer cases than `port/judge/count.txt` says. This check existed before.

Two Opus 5.5 high review rounds read this task with the task to refute it: 9 findings, then 5. Each one is resolved in the scanner or named in the limits above.

One thing is not done. The branch `main` has no protection rule on GitHub, so GitHub does not require the job `check` for a merge. The lead merges only after the job passes. A protection rule is a repository setting, which the maintainer decides.

