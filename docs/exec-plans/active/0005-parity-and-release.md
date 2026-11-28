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

None yet.
