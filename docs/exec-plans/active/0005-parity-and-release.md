# 0005 Parity, cutover, and release

Port phases 7 and 8. Starts after plan 0004. Ends with M1.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Done 2026-11-28. Fix build errors in groups that share a cause. | Fixer, Sonnet 5.5, medium | `scripts/check.sh` passes. |
| 2 | Done 2026-11-28. Run the whole judge. Work the failing list down to zero. | Lead, Fable 5.2, medium, with Sonnet 5.5 fixers | `expected-failures.txt` is empty. The case count did not go down. |
| 3 | Done 2026-11-28. Copy the skill's `scan_port.py` into `port/tools/`. Run it on `src/` with `--exclude /obj/ --exclude /bin/`. Add it and the judge to CI as merge checks. | Lead, Fable 5.2, medium | No finding. |
| 4 | Done 2026-11-28. Write the C# quickstart in `examples/Quickstart`. Run it against Neo4j with a real LLM. The quickstart is interactive (see below). | Workhorse, Sonnet 5.5, medium | The run output, and the text of one chat session. The maintainer supplies the key. |
| 5 | Close debt T1, T2, and T3: run the gate in the devcontainer, in a cloud session, and in CI. | Lead, Fable 5.2, medium | The output from each environment. |
| 6 | Run `scripts/mutate.sh`. Record the baseline score for M3. | Lead, Fable 5.2, medium | The score, in QUALITY_SCORE.md. |
| 7 | Package: README for NuGet, package icon, symbols, SourceLink, the NOTICE file inside each package. Add a root security policy. | Workhorse, Sonnet 5.5, medium | `dotnet pack -c Release` produces both packages. A scratch project installs them from a local feed and runs the quickstart. |
| 8 | Integrated adversarial pass over the full diff. | Reviewer of the maintainer's choice | No open `important` finding. |
| 9 | Blocked. The maintainer paused this task on 2026-11-28. Build the typed entity types and edge types: the section "Typed entity types and edge types" of [the public surface design](../../design-docs/public-surface-design.md). Start after the branch `port/public-surface` is on `main`. Finish before tasks 4 and 7, so that the quickstart and the package README show the typed form. First the lead gives decisions A1 to A13 a number in `port/DECISIONS.md` and adds the class name to `GraphTypeDefinition` (decision A4). Answer open points P1 to P3 of the section with a test before the reader depends on them. Use a stack of small pull requests: the builder change, the reader with the attributes, the ontology with the registration, and the read path. | Workhorse, Opus 5.5, medium. The lead reviews each layer. One adversarial review for each layer before its commit. | `scripts/check.sh --test` passes, and the judge result does not change. Typed classes for the five judge fixtures give the same names, descriptions, fields, and schema text as the fixture descriptors of the judge host. The edge names are `LIKES` and `WORKS_AT`. One end-to-end test with the scripted chat client makes two calls, one with the typed ontology and one with the lower-level lists. Both send the same model requests. One end-to-end test reads a result with `Is<T>()` and `As<T>()`. One test exists for each row of the error table. |
| 10 | Add a GitHub workflow that publishes both packages to nuget.org with NuGet trusted publishing. A version tag starts it. It runs the gate, packs both packages, and pushes them with a short-lived key from the OIDC token of the run. The repository holds no API key. Start after task 7. | Workhorse, Sonnet 5.5, medium | The workflow file, and one run that packs both packages and stops before the push. The first real push needs the maintainer's approval. |

## The interactive quickstart (task 4)

The maintainer asked for this form on 2026-11-28:

- The example ingests one piece of legislation of 150 to 200 pages into the graph.
- An agent of Microsoft Agent Framework answers questions. The graph search of Graphiti.Net is a tool of the agent.
- An AG-UI HTTP server exposes the agent.
- A CopilotKit page is the chat front end, and it talks to the server over AG-UI.
- The example uses the OpenAI model `gpt-6-luna`. The maintainer first named `gpt-6.1-luna`. The account has no model with that name, and the maintainer chose `gpt-6-luna` on 2026-11-28. The model name is a setting of the example.

These libraries are dependencies of the example only. The two packages of the product do not reference them.

The maintainer decided the split and the metadata on 2026-11-28:

- The example splits the Act by section: one episode for each section. A section with many definitions (section 5) becomes several episodes of about 400 words, each with the same chain.
- Each episode carries at minimum the section id and the chain Act > Chapter > Part > Division > Subdivision > Section. A level that does not exist is left out.
- Upstream 0.32.0 declares `episode_metadata` on an episode, but no save query writes it and `add_episode` has no parameter for it. So the port stores no free metadata, and the example keeps the metadata in three stored places: `Name` holds the section id, `SourceDescription` holds the chain in one line, and the first lines of `Content` hold the chain with the heading text of each level. The domain ontology of 2026-11-28 took the chain out of `Content` (see the results of task 4). The example also writes `sections.json`, a typed record of each episode, for the search tool.

## Gate 4

The maintainer sees the full judge pass and the scan result, and accepts or rejects each remaining `TODO(port)` marker by name.

## Release

Publishing `Graphiti.Net` and `Graphiti.Net.Neo4j` version `0.1.0-preview.1` to nuget.org needs the maintainer's explicit approval.
The maintainer decided on 2026-11-28 that a GitHub workflow publishes the packages (task 10). The maintainer's nuget.org account already has trusted publishing for the GitHub account, so no API key is stored.
The trusted publishing policy on nuget.org must name this repository and the file name of the workflow.
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

`port/tools/scan_port.py` started as a copy of the scanner of the port skill. `scripts/check.sh` runs it on `src/` after the format step, so each local gate and the CI job `check` run it. It keeps the six checks and the options of the skill's scanner, and it differs in five ways:

- It has two more options: `--allow FILE` and `--write-allow`.
- It reads the text of each file from left to right. So it knows each comment, each string of each kind, the depth of the braces, and the type and the member that hold a line. The skill's scanner reads each line alone.
- It finds the null-forgiving `!` operator after a name, a closing parenthesis, or a closing square bracket, when no `=` follows, and inside the holes of an interpolated string. The skill's scanner finds it only before five characters.
- It finds three more forms that switch a warning off: `# pragma` with a space, `#nullable disable`, and each attribute whose name ends with `SuppressMessage`.
- A file that it cannot read to its end gives the finding `scanner lost its place`: a comment or a string with no end, or braces that do not match. No allow file can accept that finding.

| Finding of the scanner on `src/` | Lines | State |
| --- | --- | --- |
| `stub` | 186 | In the allow file. 115 lines throw `NotImplementedException` where upstream raises `NotImplementedError`. 71 lines catch it where upstream catches `NotImplementedError`. |
| `port TODO` | 14 | In the allow file. 7 `TODO(port)` markers of 2 lines each. `port/STATE.md` lists them under "Blocked units". |
| `null-forgiving operator` | 0 | The first run of the wider pattern found one use in `Graphiti.cs`. Pull request 60 removed it. |
| `dynamic type`, `warning switched off`, `calls Python` | 0 | Nothing to accept, and the allow file cannot hold these labels. |

The evidence of task 3 in the plan table is "No finding". That is not the state: with no allow file the scanner reports 200 findings. The gate passes because `port/tools/scan_port_allow.txt` names each of them.

The allow file is a decision of the lead (D54), and it is not the acceptance of the maintainer. Item 5 of "Decisions for the maintainer" in `port/STATE.md` says that the maintainer accepts the stub sites by name at gate 4, and the `TODO(port)` markers wait for the same gate. The allow file is that list of names: 200 lines, one for each finding. Each line has a path, a label, a count, the member that holds the line, and the text of the line. The member is the name of the type and the first line of the declaration. Until gate 4 the file keeps the gate green and stops a new shortcut.

What fails the scan: a finding that the file does not name, a count that differs, a line of the file that the scan does not find, two lines of the file for one key, and a line of the file with a label other than `stub` and `port TODO`.

The scanner is not weaker than the skill's scanner on this code. On `src/` both report the same 200 lines. On `tests/` the skill's scanner reports 1,388 lines and this one reports 1,603: each line of the first is a line of the second, and the 215 more lines are uses of the `!` operator.

Seeds, each one undone, each with exit code 1:

- A `dynamic` field, a `!` operator, a new `TODO(port)` line, and a `#pragma warning disable` in `Helpers.cs`.
- One stub replaced by another exception, with a new stub in a new member: a method of `Nodes.cs`, a method with a tuple in its return type, and a property.
- A `throw new NotImplementedException()` added to a `catch` line that the file names.
- A `catch (NotImplementedException)` moved from `SaveAsync` of one class to a member of another class in `Edges.cs`.
- A `TODO(port)` comment moved to the end of `SearchUtils.cs`.
- The operator in the hole of an interpolated string, after a space before a dot, and after a string that holds `//`.
- The operator after a verbatim string that ends with a quote, after a verbatim string of two lines that holds `/*`, after two raw strings on one line, in the hole of a raw string, and next to a character literal that holds a quote.
- A line with three shortcuts after the raw strings of `Eval.cs`, where the first form of the reader lost its place.
- A verbatim string with no end: the finding `scanner lost its place`.

The words `Done!` in a string, in a line comment, and in a block comment gave no finding.

Limits of the scan, which a reviewer still covers:

- The scanner reads text and not syntax. It finds the member of a line from the braces and from the first line of each declaration.
- It does not see a line that moves inside its member, or a new line with the same text as a line that leaves the same member.
- It does not see the operator after a space, unless a dot and a name follow. It does not read the directives of the preprocessor, so it reads code in a branch that is off.
- It does not see `<NoWarn>` in a project file, a severity in `.editorconfig`, an exception of another type that stands for unported code, or an empty method body.
- `--exclude /bin/` and `--exclude /obj/` also skip a source folder with one of those names. `src/` has none.
- The check `calls Python` matches the text `"python"` in each string, as in the skill's scanner. `src/` has no such line.
- It scans `src/` only. The plan names that folder. The example and the test projects are not scanned.

The CI job `check` ran the judge for each pull request before this task, because `scripts/check.sh --test` runs it. Two facts about the judge in the gate:

- The judge call of `scripts/check.sh` now has `--verbose`. A case that fails and is not listed, or that is listed and bound and wrong, then prints 20 difference lines, where it printed 3 (debt T32).
- The count of judge cases cannot go down: the judge fails if it runs fewer cases than `port/judge/count.txt` says. This check existed before.

Three Opus 5.5 high review rounds read this task with the task to refute it: 9 findings, then 5, then 4. Each finding about the scanner is resolved in its code or named in the limits above. After round 3 the reader of the scanner changed one more time, and the comparison with the skill's scanner and the seeds above are its evidence.

One thing is not done. The branch `main` has no protection rule on GitHub, so GitHub does not require the job `check` for a merge. The lead merges only after the job passes. A protection rule is a repository setting, which the maintainer decides.

### Task 4, the interactive quickstart, 2026-11-28

The example is in `examples/Quickstart`, and its README gives each command. The example builds with the solution, so `scripts/check.sh` builds and formats it.

The run output of the second full ingestion, on Neo4j with `gpt-6-luna` and a window of 3 episodes:

- `split` gives 250 episodes from 246 sections (16 Parts, 30 Divisions, 9 Subdivisions). Section 5 becomes 5 episodes. The text keeps its hyphens: `end-user` appears 155 times and `enduser` 0 times.
- The ingestion used 5.45M input and 0.395M output tokens, about 0.37 USD, in about 30 minutes. Its last line: `Episodes ingested: 47. Entities returned: 173. Facts returned: 113. Time: 00:04:50.`
- The graph of the group `online-safety-act-2021` holds 250 episodes, 383 entities, and 606 facts. 9 episodes have no fact.
- The first full ingestion cost about 0.36 USD. The two full runs cost about 0.73 USD.

The text of one chat session, from the page in a browser:

> Question: Who appoints the eSafety Commissioner, and for how long?
>
> Answer: The Minister appoints the eSafety Commissioner by written instrument. The appointment lasts for the period specified in that instrument, up to a maximum of five years. (Online Safety Act 2021 ss 167–168)

Sections 167 and 168 of the Act state this. Five more questions went through the AG-UI endpoint. Each cited section id was in the tool results, and each statement was supported by the text of the Act. Two limits: one answer about a removal notice named only the hosting service provider (section 90, not section 88), and one answer held one inference.

What the run found about the product:

| Finding | Evidence | Where it goes |
| --- | --- | --- |
| The default chat adapter asks for JSON mode and puts the schema in the prompt text. A small model then returns the wrong shape for some calls. | With `gpt-6-luna`, 6 of 14 trial runs of five sections failed with `ValidationException: EdgeDuplicate`. The example class `StructuredOutputChatClient` sends the schema as native structured output. With it, 1 of 17 trial runs failed, and each full run failed 2 times in about 1,150 `resolve_edge` calls. | Debt T33. |
| OpenAI rejects the schemas of Graphiti in strict mode. | The schemas have `$defs` and optional properties. `AIJsonUtilities.TransformSchema` closes only the root object. | Debt T33. |
| `RemoveEpisodeAsync` took about 5 minutes for one episode of the full graph. | Upstream `remove_episode` rebuilds the summary of each entity that stays from all its other episodes. The entity of the Act has 250 episodes. This is upstream behavior, not a difference of the port. | The README of the example. `reset` deletes each node with `DeleteAsync`. |
| A failed ingestion leaves episodes with no entity. | `AddEpisodeAsync` saves the episode before the extraction. Each failed run left 3 such episodes with a window of 3. `--resume` finds them and ingests them again. | The README of the example. |
| `gpt-6-luna` rejects function tools together with reasoning in the Chat Completions API. | The provider error. The agent turns the reasoning effort off. | The README of the example. |
| The batch delete of a group fails on Neo4j. | GAPS U1. | Known before this task. |

One Opus 5.5 high review read the example with the task to refute it: 14 findings. Each one is resolved in the code or stated in the README. The fix round had no second review. The lead read its diff, ran `split`, tested the host filter (400 for another host), and asked one new question in the browser. The lead then found that the page listened on each network address, and bound it to the loopback address.

The domain ontology, 2026-11-28. The maintainer asked for an ontology of the domain in place of the structure of the document, after the post <https://justinbarias.io/blog/holodeck-temporal-memory-part1/>. The maintainer approved 7 entity types and 12 relationship types, and the exclusion of the default type `Entity`. The option `StrictEdgeTypes` is not set, because `AddEpisodeOptions` does not have it. The file `examples/Quickstart/Quickstart.Server/Ontology.cs` holds the types, and the README of the example describes them. The content of an episode now has no chain line and no section number. The section id and the chain stay in `Name` and `SourceDescription`.

- One Opus 5.5 high review read the change with the task to refute it: 14 findings. The README and the code resolve 12. The maintainer decided the other two. The instructions now name an instrument with its scheme, and each episode starts with a topic line from the heading text of the Part and the Division. `HAS_DUTY` also joins an authority to a duty, and a 13th type `APPLIES_TO` joins an enforcement measure to a party.
- Command, after these changes: `ingest --sections 6,13,30,88,91,92,163,167` with `QUICKSTART_GROUP_ID=osa-2021-ontology`. Its last lines: `Episodes ingested: 8. Entities returned: 55. Facts returned: 48. Time: 00:01:35.` Token use: 286,734 input and 28,852 output, about 0.02 USD. The run before the review cost the same.
- The group holds 35 entities, each with one of the 7 types, and 48 facts. No fact only states the place of a provision. 39 facts use one of the 13 relationship types, one has the name `RELATES_TO`, and 8 have a name that the model made.
- The two removal notices of the sample (cyber-bullying material, cyber-abuse material) are two entities.
- Two questions went through the AG-UI endpoint on port 8002. The second: who can complain about cyber-bullying material, what a provider must do after a removal notice for cyber-abuse material, and the penalty. The answer cited sections 30, 88, and 91. Each id was in the tool results, and the text of the Act supports each statement.
- `scripts/check.sh` passed.
- The full ingestion with the ontology, 2026-11-28. The maintainer authorized it, and the lead session ran `reset` and `ingest` on the group `online-safety-act-2021`. The lead session reports 5 runs, 4 stops with `ValidationException: 2 validation errors for EdgeDuplicate`, 9.90M input and 0.816M output tokens, about 0.70 USD, and about 42 minutes.
- Read back from Neo4j after the run: 250 episodes, 369 entities (130 Duty, 75 Instrument, 73 Party, 31 Authority, 30 OnlineService, 19 Material, 11 EnforcementMeasure), and 1,110 facts. No entity is a section, a Part, or the Act, and no fact only states the place of a provision. The graph before the ontology had 606 facts, and 140 of them only stated a place.
- 700 of the 1,110 facts use one of the 13 relationship types. 45 have the name `RELATES_TO`, and 365 have one of 148 names that the model made. The types do not cover the review of decisions, the disclosure of information, or the investigation of complaints.
- The validation error occurred 4 times in this run, and 2 times in each run before the ontology (debt T33).
- The fix round after the review had no second review.

The Digital Duty of Care Bill 2026 and the rename, 2026-11-28. The maintainer asked for a second piece of legislation in the same group, and for the name "Australian Online Safety Legislative Agent". The Bill passed both Houses on 2026-11-25 and has no Royal Assent. So the example uses that day as a placeholder for the assent (the setting `QUICKSTART_BILL_ASSENT_DATE`). The class `BillSplitter` splits the Bill, and `ingest --bill` ingests it. No file under `src/` changed.

- `split --bill` gave 226 episodes for the full run: 109 new sections, 58 changes of words, and 59 repealed sections. After the review it gives 228 episodes and leaves 89 items out, and it prints each one. 72 episodes have the reference time 2026-11-26 (Schedule 1), and 154 have 2027-11-25 (Schedules 2 and 3).
- The trial of 9 episodes went through `AddEpisodeAsync` and cost about 0.03 USD. Graphiti ended 1 old fact (the penalty of section 91). It ended 0 of the 87 old facts about the basic online safety expectations: for a repeal, the model wrote new facts of the form "Until 25 November 2027 ..." with an end date of their own. The session of this work deleted the 5 repeal episodes of this trial and the 44 facts that only they made, with Cypher on the group.
- So a repeal makes no call to the chat model. Each fact that ends costs one call to the embedding model. The command saves one episode for the repeal, and it sets `InvalidAt` on each fact whose source episodes are all repealed (`Nodes.Episode.SaveAsync`, `Edges.Entity.SaveAsync`).
- The full run: 7 runs of the command, 6 stops with `ValidationException: 2 validation errors for EdgeDuplicate` (debt T33), 6.21M input and 0.590M output tokens, about 0.46 USD, about 31 minutes. Its last lines: `Episodes ingested: 19. Entities returned: 73. Facts returned: 58.` and `Repeals applied: 59. Facts ended: 279.` The trial ended 30 more.
- Read back from Neo4j: 476 episodes, 594 entities (each with one of the 7 types), and 1,768 facts. 327 facts have an end date: 309 from the repeal step and 18 from the contradiction path of Graphiti. 295 of the 1,110 facts of the 2021 Act ended. 87 of 87 facts of the Act about the basic online safety expectations ended, and 65 of 71 about an industry code or standard.
- Two questions went through the AG-UI endpoint on port 8002: the repeal of the basic online safety expectations with its date and the new duty, and the penalty of section 91 before and after the Bill. Each cited id was in the tool results, with its note for the Bill.
- Not checked: the licence of the Bill text (the repository holds none of it), a browser run of the renamed page (only `tsc --noEmit` ran), and the items that name a provision that the 2021 text of the Act does not have. 
- The page: a source label of the facts table wraps and shows a section one time with the change of the Bill as a note. The validity ruler names each fact by its two entities, has room after the last date, and marks today with a line (`web/DESIGN.md`). Looked at in a browser at 1440 by 900 and at 375 by 812.
- One Codex review (gpt-6-astra, high effort) read the Bill work with the task to refute it: 18 findings. The code resolves 11: the repeal step reads the repeals of earlier runs, it moves a later end date to the day of the repeal, and the tool keeps the repeal source of a fact. A bare number and a change of "and" to "or" are no longer trivial, the split reports each transitional provision and each reason, `split --bill` needs no key, an option needs its value, the date rule handles an assent on 29 February, and the agent says "placeholder" only for the placeholder date. The README states the other 7 as limits or corrects its text. The new rule for trivial items gives 2 more episodes (Schedule 2 item 48 and Schedule 3 item 13). They are not in the graph yet. The fix round had no second review.

Not done:

- Task 9 is paused by the maintainer (2026-11-28), so the example does not show the typed C# classes. It builds its types with `GraphTypeDefinition`.
- The example has no test of its own, and the scanner does not read it.
- Other models, and strict mode with changed schemas, were not tried.

### Task 2, the decision of the maintainer, 2026-11-28

The maintainer accepted all 45 listed cases by name (decision D55). The lead ran the .NET judge host on each case first: each one answers `JudgeTypeRejected`, so no listed case runs and gives a wrong result.

The maintainer applied the judge change by hand, and pull request 70 merged it. `expected-failures.txt` is empty, and the 45 ids are in `port/judge/accepted-differences.txt`. A case there passes only while the .NET host answers `JudgeTypeRejected`. One Codex review (gpt-6-astra, high effort) found three faults in the first form of the change, and a second patch fixed them. The lead ran the final judge on a copy outside the repository:

- The whole judge: 1,999 cases, 1,999 passed, 0 expected failures, 45 accepted differences.
- An accepted id that the lead removed from the list fails as a case that is not listed.
- A passing case that the lead added to the list fails, because the host did not answer `JudgeTypeRejected`.
- An id in the list that no case has fails the run.
- An id that is in both lists fails the run.

Not explained: in one full run of the copy, the flow cases `flow.hand.guard_empty_episode` and `flow.hand.guard_invisible_chars` failed, and the next full run passed. Another session ran a gate on the shared judge database at that time (debt T32).

Not checked: one accepted case, `pure.prompts.prompt_config.LLMModel.blank_id_raises`, mixes a blank id with a wrong-type field. So the judge has no case that shows a blank id alone.
