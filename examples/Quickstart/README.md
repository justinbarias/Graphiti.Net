# Interactive quickstart: ask the Online Safety Act 2021

This example shows Graphiti.Net in use. You put one Act of legislation into the graph. Then you ask an agent about it in a chat page.

```text
 Online Safety Act 2021 (.docx)
        |  ingest: one episode for each section
        v
 Neo4j  <--  Graphiti.Net (IGraphiti: AddEpisodeAsync, SearchAsync)
        ^
        |  tool: search_legislation(query)
 Microsoft Agent Framework agent  --  AG-UI over HTTP (port 8000)
        ^
        |
 Next.js page with CopilotKit (port 3000)
```

The example uses only the public members of the two product packages `Graphiti.Net` and `Graphiti.Net.Neo4j`. The other libraries (Agent Framework, AG-UI, CopilotKit, OpenAI) are dependencies of this example only.

## What you need

- The .NET 10 SDK. Run `scripts/setup-env.sh` from the repository root to install the pinned SDK.
- Node.js 20.9 or later (the minimum of Next.js 16.4.6).
- Neo4j. Run `docker compose up -d --wait neo4j` from the repository root.
- An OpenAI API key. Put one line in the file `.env` at the repository root. The file is git-ignored.

```text
OPENAI_API_KEY=your key here
```

Only the .NET server reads the key. The web page needs no key.

## Settings

Each setting is an environment variable. The server also reads the file `.env` at the repository root. An environment variable wins over the file.

| Variable | Default |
| --- | --- |
| `OPENAI_API_KEY` | none. The server stops with one clear sentence if it is missing. |
| `NEO4J_URI` | `bolt://localhost:7687` |
| `NEO4J_USER` | `neo4j` |
| `NEO4J_PASSWORD` | the password of `docker-compose.yml` |
| `QUICKSTART_CHAT_MODEL` | `gpt-6-luna` |
| `QUICKSTART_EMBEDDING_MODEL` | `text-embedding-3-small` |
| `QUICKSTART_GROUP_ID` | `online-safety-act-2021` |

The example touches only the data of its group id: the nodes of that group in Neo4j, and the file `data/sections.<group id>.json`. It never clears the database.

## Step 1: download the Act

Download the Word file of the as-made Act from the Federal Register of Legislation:
<https://www.legislation.gov.au/C2021A00076/asmade/downloads>

Save it in `examples/Quickstart/data/`. This folder is git-ignored. The commands below call the file `online-safety-act-2021-asmade.docx`.

## Step 2: split and ingest

Check the split first. This command calls no model.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- split --file examples/Quickstart/data/online-safety-act-2021-asmade.docx
```

The Act has 246 sections. The split makes 250 episodes, because the definitions section (section 5) is too large for one episode. The splitter cuts it at a definition into 5 episodes of about 400 words.

Try five sections first. This costs a few cents at most.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --limit 5
```

Then ingest the whole Act. Use `--resume` to continue after an error: it skips each episode that is already in the group.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --resume
```

A plain `ingest` on a group that already has episodes stops with one sentence: use `--resume`, or run `reset` first. The command prints one line for each episode. At the end it prints the count of episodes, the token use of each prompt of Graphiti, and the time. The spend limit of the command is 8 million input tokens and 1 million output tokens. It counts one process: each run of the command, also each run with `--resume`, starts a new count. The command checks the limit after each episode, so the episodes of the window can pass it by a little. Use `--window N` to change the number of episodes that Graphiti works on at the same time (the default is 3, and 1 gives serial ingestion).

The full Act used about 5.4 million input tokens and 0.4 million output tokens of `gpt-6-luna`. At 0.05 USD and 0.25 USD for 1 million tokens, this costs about 0.37 USD. It took about 25 minutes with a window of 3. The graph then held 250 episodes, 383 entities, and 606 facts. Nine episodes have no fact, but the tool can still find them by their text.

## Step 3: start the server and the page

Start the AG-UI server on <http://localhost:8000>:

```bash
dotnet run --project examples/Quickstart/Quickstart.Server
```

In a second terminal, start the page on <http://localhost:3000>:

```bash
cd examples/Quickstart/web
npm install
npm run dev
```

Open <http://localhost:3000> and ask a question. Three example questions are on the page.

An example. The question: "What are the conditions for a service to be a social media service?" The answer, shortened: the service must be an electronic service whose sole or primary purpose is online social interaction between two or more end-users, it must let end-users link to or interact with other end-users, and it must let them post material (Online Safety Act 2021 s 13). Advertising and its revenue are disregarded when the purpose is judged (s 13(3)).

## How it works

- **Split.** `ActSplitter` reads the `.docx` file with `System.IO.Compression` and `System.Xml.Linq`. A section starts at a paragraph of the style `ActHead5`. The styles `ActHead2`, `ActHead3`, and `ActHead4` are the Part, the Division, and the Subdivision. This Act has no Chapter.
- **Metadata.** Upstream Graphiti 0.32.0 has no way to store free metadata on an episode. So each episode carries its place in the Act in three stored fields:
  - `Name`: the section id, for example `Online Safety Act 2021 s 36`.
  - `SourceDescription`: the chain in one line, for example `Online Safety Act 2021 (Cth) > Part 3 > Division 4 > Section 36`. A level that does not exist is left out.
  - The first lines of `Content`: the chain with the heading text of each level, then the section number and heading, then the text.

  The ingestion also writes `data/sections.<group id>.json`, one file for each group id. It holds one record for each episode, with the episode uuid. A run with `--limit` or `--resume` changes only the records of the sections that it processes, and never drops another record.
- **Reference time.** Each episode has the date of assent of the Act as its reference time: 23 July 2021.
- **Structured output.** Graphiti asks the model for JSON and puts the schema in the prompt text, and a model can copy the wrong shape from that text. The class `StructuredOutputChatClient` reads the schema of each Graphiti call from `GraphitiModelCall.Current` and sends it to OpenAI as native structured output. The section "Call information for host middleware and for the judge" of `docs/design-docs/public-surface-design.md` (decision D17) gives the host this extension point.
- **The ingestion can fail with a validation error.** The class does not ask for strict mode, because OpenAI rejects the schemas of Graphiti in strict mode (they have `$defs` and optional properties). In the non-strict mode the model still sometimes returns the wrong shape. Then Graphiti stops with `ValidationException: ... EdgeDuplicate`. In the trial runs of five sections this happened in 1 of 17 runs with the class and in 6 of 14 runs without it. The full run of the Act (about 1,100 calls of this kind) failed 2 times. Run the command again with `--resume`: it skips the finished episodes and processes an unfinished episode again.
- **Agent.** One agent of Microsoft Agent Framework. It turns the reasoning effort of the model off, because `gpt-6-luna` does not accept function tools together with reasoning in the Chat Completions API. It has one tool, `search_legislation(query)`. The tool calls the graph search of `IGraphiti` for the group id. The result has two lists. The list `facts` has, for each fact, the fact text, the validity dates, the section id, and the chain. The list `sections` has the text of the sections that state these facts, and the text of sections that the search found by their own text (at most 6 sections, no section two times, each cut to 2,000 characters after the chain line). The tool uses `AdvancedSearchAsync` with the recipe `CombinedHybridSearchRrf` for edges and episodes, so a section with no fact can still be found. The tool reads this text from the graph, because Graphiti stores the content of each episode. The instructions of the agent say: answer only from the tool results, quote or restate the words of the section, cite the section id for each statement, and say so if the graph has no answer. The agent must cite only a section id that is in the tool results of this conversation. A small model can still break this rule: in one test it cited section 80 for a penalty that is in the text of other sections, because the graph held only structural facts of section 80.

About a quarter of the facts in the graph (140 of 606 in the last full run) state the place of a section in the Act (for example "Section 70 is within Part 5"). This follows from the chain lines at the top of each episode. These facts take some of the slots of a search result. About 12 percent of the episodes (30 of 250) have more than 2,000 characters after the chain line, so the tool cuts their text, and the agent can miss the end of such a section.
- **Page.** A Next.js app with CopilotKit. The route `/api/copilotkit` forwards each chat request to the AG-UI server with an `HttpAgent`.

## Other commands

`reset` deletes the data of the group id of the example, and the sections file of that group, and nothing else. It asks you to type `yes`. It deletes each episode and each entity of the group with `DeleteAsync`, which also deletes the facts of the node. It makes no model call and takes a few seconds. (The batch delete of Graphiti, `DeleteByGroupIdAsync`, fails on Neo4j. `RemoveEpisodeAsync` works, but it asks the model to rebuild the summaries of the entities that stay, and it took about 5 minutes for one episode of the full graph.)

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- reset
```

## Security

The server and the page listen on the loopback address only. The server has no authentication, and it holds your OpenAI key. It rejects a request that names another host. Do not put the server or the page on a network.

The tool puts stored text of the Act into the prompt of the agent. The agent treats that text as data. Its one tool only reads the graph.

## Privacy

- Graphiti.Net sends no usage telemetry.
- The page turns the CopilotKit telemetry and the Next.js telemetry off in its npm scripts (`COPILOTKIT_TELEMETRY_DISABLED=true`, `NEXT_TELEMETRY_DISABLED=1`), and it turns the CopilotKit inspector off. The file `web/package.json` also turns off the install ping of the `@scarf/scarf` package.
- The page uses no CopilotKit cloud account, and it needs no key.
- The text of each episode and each question goes to the OpenAI API. The key never goes to the page.

## Attribution

The text of the Online Safety Act 2021 (Cth) comes from the Federal Register of Legislation (<https://www.legislation.gov.au/>). It is published under the Creative Commons Attribution 4.0 International licence (<https://creativecommons.org/licenses/by/4.0/>). See the terms of use at <https://www.legislation.gov.au/terms-of-use>.

This example changes the text. It splits the text into sections, and it adds the heading of each level before the text of each section. It also turns each tab into one space and keeps each hyphen (a non-breaking hyphen of the Word file becomes a plain hyphen, for example in "end-user" and "cyber-abuse"). It changes no word. The Commonwealth Coat of Arms is not part of the licence. This example does not include it.
