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

## The interactive quickstart (task 4)

The maintainer asked for this form on 2026-11-28:

- The example ingests one piece of legislation of 150 to 200 pages into the graph.
- An agent of Microsoft Agent Framework answers questions. The graph search of Graphiti.Net is a tool of the agent.
- An AG-UI HTTP server exposes the agent.
- A CopilotKit page is the chat front end, and it talks to the server over AG-UI.
- The example uses a recent OpenAI model. The maintainer named `gpt-6.1-luna`. The quickstart makes sure that the account has the model before the first run, and the model name is a setting of the example.

These libraries are dependencies of the example only. The two packages of the product do not reference them.

## Gate 4

The maintainer sees the full judge pass and the scan result, and accepts or rejects each remaining `TODO(port)` marker by name.

## Release

Publishing `Graphiti.Net` and `Graphiti.Net.Neo4j` version `0.1.0-preview.1` to nuget.org needs the maintainer's explicit approval and the maintainer's API key.
Keep the Python oracle runnable and the judge able to compare both builds for as long as the maintainer wants a way back.

## Results

None yet.
