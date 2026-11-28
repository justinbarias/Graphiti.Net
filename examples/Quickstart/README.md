# Australian Online Safety Legislative Agent: the interactive quickstart

This example shows Graphiti.Net in use. You put two pieces of Australian legislation into one graph: the Online Safety Act 2021 (Cth), and the Online Safety Amendment (Digital Duty of Care) Bill 2026, which amends the Act. Then you ask an agent about them in a chat page. The Bill shows the temporal side of the graph: facts of the Act end on a date, and new facts start.

```text
 Online Safety Act 2021 (.docx)          Digital Duty of Care Bill 2026 (.docx)
        |  ingest: one episode for         |  ingest: one episode for each new section
        |  each section, with the          |  and each change, and an end date for the
        |  ontology of the domain          |  facts of each repealed section
        v                                  v
 Neo4j  <--  Graphiti.Net (IGraphiti: AddEpisodeAsync, SearchAsync)
        ^
        |  tool: search_legislation(query)
 Microsoft Agent Framework agent  --  AG-UI over HTTP (port 8000)
        ^
        |
 Next.js page with CopilotKit (port 3000)
```

The graph uses an ontology of the domain that the Act regulates: authorities, parties, services, material, instruments, duties, and enforcement measures. It does not copy the structure of the document. The section "The ontology" explains this.

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
| `QUICKSTART_BILL_ASSENT_DATE` | `2026-11-25`. This is a placeholder: see "Step 3". |

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

Try five sections first. This costs a few cents at most. Use `--sections 6,13,30,88,91` in place of `--limit 5` to choose the sections by number.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --limit 5
```

Then ingest the whole Act. Use `--resume` to continue after an error: it skips each episode that is already in the group.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --resume
```

A plain `ingest` on a group that already has episodes stops with one sentence: use `--resume`, or run `reset` first. The command prints one line for each episode. At the end it prints the count of episodes, the token use of each prompt of Graphiti, and the time. The spend limit of the command is 8 million input tokens and 1 million output tokens. It counts one process: each run of the command, also each run with `--resume`, starts a new count. The command checks the limit after each episode, so the episodes of the window can pass it by a little. Use `--window N` to change the number of episodes that Graphiti works on at the same time (the default is 3, and 1 gives serial ingestion).

The full Act with the ontology used about 9.9 million input tokens and 0.82 million output tokens of `gpt-6-luna`. At 0.05 USD and 0.25 USD for 1 million tokens, this costs about 0.70 USD. It took about 42 minutes with a window of 3, in 5 runs: 4 runs stopped with a validation error (see "How it works"), and `--resume` continued each one. One run can also stop at the spend limit, and `--resume` continues it too. The graph then held 250 episodes, 369 entities, and 1,110 facts. 14 episodes have no entity, but the tool can still find them by their text.

The run before the ontology cost about 0.37 USD and gave 383 entities and 606 facts, and 140 of those facts only stated the place of a section. A sample of 8 sections (6, 13, 30, 88, 91, 92, 163, 167) costs about 0.02 USD and takes about 1 minute 35 seconds.

If a group holds a graph from before the ontology, run `reset` before you ingest. A run with `--resume` on such a group mixes the two forms.

## Step 3: add the Bill

The Online Safety Amendment (Digital Duty of Care) Bill 2026 amends the Act. Both Houses of the Parliament passed it on 25 November 2026. The Bill had no Royal Assent at the time of this example. So it was not an Act, and the Federal Register of Legislation did not have it.

Download the Word file of the Bill "as passed by both Houses" from the Parliament of Australia (<https://www.aph.gov.au/>, Bills and Legislation). Save it in `examples/Quickstart/data/`. The commands below call the file `ddoc-bill-2026.docx`.

Each date of the Bill counts from the day of the Royal Assent. The example uses the placeholder 25 November 2026 for that day. Set the variable `QUICKSTART_BILL_ASSENT_DATE` to the real day when the Bill has assent. The agent then no longer says that the dates are placeholders. A new date does not change a group that already holds the Bill: run `reset`, and ingest the Act and the Bill again. With the placeholder, Schedule 1 commences on 26 November 2026, and Schedules 2 and 3 commence on 25 November 2027 (section 2 of the Bill).

Check the split of the Bill first. This command calls no model. It prints each episode, and each item that the split leaves out with the reason.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- split --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --bill examples/Quickstart/data/ddoc-bill-2026.docx
```

Then ingest the Bill into the same group as the Act. The command needs both files. It reads the heading of each changed provision, and the text of each repealed provision, from the Act. Ingest the Act first. The command never deletes the episodes of the Act.

```bash
dotnet run --project examples/Quickstart/Quickstart.Server -- ingest --file examples/Quickstart/data/online-safety-act-2021-asmade.docx --bill examples/Quickstart/data/ddoc-bill-2026.docx --resume
```

If the command stops with an error, run it again: it skips each finished episode. The command applies the repeals at the end, after each model episode. So the graph is complete only after a run that ends with no error.

Use `--items 2:13,1:77` to ingest only some items (Schedule 2 item 13, and Schedule 1 item 77). The options `--items` and `--limit` are for a trial. An item can repeal a section and insert its replacement, and `--limit` can select only one of the two.

## Step 4: start the server and the page

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

Open <http://localhost:3000> and ask a question. Three example questions are on the page. The page has the name "Australian Online Safety Legislative Agent".

The page has two areas. The left area is the conversation. Each call of the tool `search_legislation` is one row there, and the row changes as the events of the call arrive. The right area shows the result of the selected call:

- One button for each tool call of the conversation ("Search 1", "Search 2"), if the agent made more than one call.
- The four AG-UI events of the call (`TOOL_CALL_START`, `TOOL_CALL_ARGS`, `TOOL_CALL_END`, `TOOL_CALL_RESULT`), each marked when it arrives.
- The temporal graph. An entity is a circle. A fact is a line from its source entity to its target entity, with the name of the relation. The colour of the line is the first section that states the fact. A fact with an end date is a broken line.
- The fact that you point at, in full: the two entities, the relation, the validity dates, the sections, and the fact text.
- The validity ruler. Each fact is one line on a time axis from the first date of the result to today, with its dates at the right. A fact with no end date ends in an arrow.
- One button for each section that states a fact. Point at a button, a line, or a table row, and the page shows only those facts. Select it to keep that view.
- The fact table. Each row has the sections, the two entities, the relation, the two validity dates, and the fact text.
- The text of each section, and the JSON text of the result.

To use other ports, set `QUICKSTART_URL` for the server (for example `http://localhost:8001`), set `MS_AGENT_URL` for the page (for example `http://localhost:8001/`), and start the page with `npx next dev --hostname 127.0.0.1 --port 3001`.

An example. The question: "What are the conditions for a service to be a social media service?" The answer, shortened: the service must be an electronic service whose sole or primary purpose is online social interaction between two or more end-users, it must let end-users link to or interact with other end-users, and it must let them post material (Online Safety Act 2021 s 13). Advertising and its revenue are disregarded when the purpose is judged (s 13(3)).

## The ontology

The ontology models the domain, and not the document. A knowledge graph of legislation drifts toward the structure of the text: each section, Part, and cross-reference becomes a node or a fact. An earlier form of this example had that problem. About a quarter of its facts (140 of 606) only stated the place of a section, for example "Section 70 is within Part 5". Such a fact repeats what the episode already records, it takes a slot of each search result, and it costs model calls. The post <https://justinbarias.io/blog/holodeck-temporal-memory-part1/> gives the rule: model what the rules are about, and keep the section number as provenance.

The file `Quickstart.Server/Ontology.cs` holds the ontology. It uses `GraphTypeDefinition`, which is the form of custom types that Graphiti.Net has today.

The entity types:

| Type | Definition | Example from the Act |
| --- | --- | --- |
| `Authority` | An office, a body, or a court that holds a power or a function | eSafety Commissioner, Minister, Federal Court |
| `Party` | A kind of person that the Act protects or regulates | Australian child, end-user, provider of a social media service |
| `OnlineService` | A kind of service that the Act regulates | social media service (section 13) |
| `Material` | A kind of content that the Act regulates | cyber-bullying material targeted at an Australian child (section 6) |
| `Instrument` | A notice, a request, a code, a standard, or a determination made under the Act | removal notice for cyber-abuse material (section 88), industry code |
| `Duty` | Something that a party or an authority must do or must not do | comply with a removal notice for cyber-abuse material (section 91) |
| `EnforcementMeasure` | A penalty, or another measure that follows a contravention | civil penalty of 500 penalty units (section 91), formal warning (section 92) |

The types `OnlineService`, `Material`, and `Instrument` have one attribute, `definition`.

The relationship types:

| Type | From, to | Definition |
| --- | --- | --- |
| `MAY_ISSUE` | Authority, Instrument | The authority has the power to make or give the instrument |
| `ISSUED_TO` | Instrument, Party | The instrument is given to this kind of party |
| `CONCERNS` | Instrument, Material | The instrument acts on this kind of material |
| `IMPOSES` | Instrument, Duty | The instrument creates the duty. Attribute: `time_limit` |
| `HAS_DUTY` | Party or Authority, Duty | The party or the authority must do what the duty states |
| `ENFORCED_BY` | Duty, EnforcementMeasure | The measure follows a contravention. Attribute: `penalty_units` |
| `MAY_APPLY` | Authority, EnforcementMeasure | The authority can use the measure |
| `APPLIES_TO` | EnforcementMeasure, Party | The measure can be used against this kind of party |
| `PROVIDES` | Party, OnlineService | The party supplies the service |
| `PROVIDED_ON` | Material, OnlineService | The material counts if it is on this kind of service |
| `TARGETED_AT` | Material, Party | The material harms this kind of party |
| `MAY_COMPLAIN_ABOUT` | Party, Material | The party can complain about the material |
| `HAS_POWER_OVER` | Authority, Authority | One authority appoints, directs, delegates to, reviews, or terminates the other. Attribute: `power` |

Five members of `AddEpisodeOptions` and one change to the episodes apply the ontology:

- `EntityTypes` and `EdgeTypes` give the types. `EdgeTypeMap` gives the pair of entity types of each relationship type. The map does not forbid other names. If the model uses one of the 13 names on a wrong pair, Graphiti renames the fact to `RELATES_TO` and clears its attributes. A name that the model makes passes for each pair.
- `ExcludedEntityTypes` holds `Entity`, the default type of Graphiti. Graphiti then takes this type out of the list in the prompt, so the model must give each entity one of the seven types. This is a strong hint, and it is not a guarantee. Graphiti drops an entity only if the model returns a type outside the list. If a fact names an entity that was dropped, Graphiti saves that entity again as a plain `Entity`. The sample has no such node.
- `CustomExtractionInstructions` tells the model that a section number is where a rule lives, and not what the rule is about. It also says that a cross-reference is not a fact. The Act has many schemes, and each scheme has notices of its own. So the instructions also tell the model to name an instrument or a duty with its scheme, for example `removal notice for cyber-abuse material`.
- The content of each episode has no chain line and no section number (see "Provenance" below). Its first line is a topic: the heading text of the Part and the Division, with no number. For example `Topic: Cyber-abuse material targeted at an Australian adult, Removal notices`. The topic tells the model the scheme of the section.

The result of the full run:

- Each of the 369 entities has one of the seven types: 130 Duty, 75 Instrument, 73 Party, 31 Authority, 30 OnlineService, 19 Material, and 11 EnforcementMeasure. No entity is a section, a Part, a Division, or the Act.
- No fact only states the place of a provision.
- 700 of the 1,110 facts (63 percent) use one of the 13 relationship types. 45 facts have the name `RELATES_TO`. For the other 365 facts the model made a name of its own, and there are 148 such names. The most frequent are `REVIEWABLE_BY` (18), `DISCLOSES_INFORMATION_TO` (14), `NOT_DEEMED_PROVIDED_MERELY_BY` (12), and `MAY_INVESTIGATE` (10). The 13 types do not cover the review of decisions, the disclosure of information, or the investigation of complaints.
- The removal notices of the schemes are separate entities, for example `removal notice for cyber-abuse material` and `removal notice for class 1 material`. The model did not always use one name for one thing: the graph also has `removal notice for intimate images` and `removal notice for an intimate image`, and one bare `removal notice`.

Limits:

- A definition has one entity, and a fact needs two. The attribute `definition` is there for the conditions of a definition. The model filled it for 72 of the 124 entities of the three types that have it. The summary of the entity and the section text that the tool returns also hold the definition.
- Graphiti has no rule that a relationship holds one value at a time. A later version of this example can ingest a second version of the Act to show how facts change. The section id of each episode is then the key to compare the two texts.
- Typed C# classes for the ontology wait for task 9 of plan 0005.

## How the Bill changes the graph

A Bill that amends an Act is a list of items, and each item changes the Act. The class `BillSplitter` makes three kinds of episode from the items that amend the Online Safety Act 2021:

| Kind | From | What the ingestion does |
| --- | --- | --- |
| A new section | An item that inserts sections, for example the new Part 2 "Digital duty of care" | One episode for each section, through `AddEpisodeAsync`. The facts start on the commencement day. |
| A change of words | An item that omits, substitutes, or adds words, for example a new penalty | One episode for each item, through `AddEpisodeAsync`. Graphiti can end an old fact that the new fact contradicts. |
| A repeal | An item that repeals a Part, a Division, or a section, for example Part 4 "Basic online safety expectations" | One episode for each repealed section, and no call to the chat model. The command ends the facts of the section itself. |

The reference time of each episode is the day on which its Schedule commences. The first line of the content says if the rule is new or changed, and gives the date. The ontology is the same as for the Act: the Bill needed no new type.

The split leaves these items out: each item that amends another Act, the technical corrections of Schedule 4, the items that update a reference to the Regulatory Powers Act, each change of a heading or a note, each change of only a cross-reference or the punctuation, the lists of provisions (for example the provisions that an infringement notice can enforce), and the application and transitional provisions. It also leaves out an item that repeals a paragraph or a subsection with no new words, because the Bill does not give the words that end. The facts of such a paragraph stay open in the graph. The command `split --bill` prints each one.

A repeal makes no call to the chat model, for this reason. Graphiti ends an old fact only if a new fact with a later start date contradicts it, and a model decides that. In a trial of 5 repealed sections, the model wrote new facts that start with "Until 25 November 2027". None of the 87 old facts about the basic online safety expectations got an end date. But a repeal is a fact about the text: the section stops being law on a known day. So the command `ingest --bill` does this for each repealed section:

- It saves one episode for the repeal with `Nodes.Episode.SaveAsync`. The episode is the source of the change, and an answer can cite it.
- It finds the source episodes of the section that are older than the repeal: the episode of the Act, and each episode of the Bill that changed the section before.
- It sets `InvalidAt` to the day of the repeal on each fact whose source episodes are all repealed, with `Edges.Entity.SaveAsync`. It also adds the repeal episode to the sources of the fact. A fact that another section also states stays open. Each save makes one call to the embedding model, because Graphiti embeds the fact text again.

This is the rule of the post that the section "The ontology" names: when the source has versions, let the text decide which facts end. The Bill amends the current compilation of the Act, and this example holds the Act as made in 2021. A section number is the same in both, but an item can name a provision that the 2021 text does not have. For a change of words, the split still makes an episode, without the heading of the section. A repeal of such a provision is left out, because the graph holds no fact of it.

The result of the full run of the Bill, with the placeholder date:

- The split gives 228 episodes: 109 new sections, 60 changes of words, and 59 repealed sections. It leaves 89 items out.
- The 169 model episodes used about 6.2 million input tokens and 0.59 million output tokens of `gpt-6-luna`, about 0.46 USD, in about 31 minutes. The command stopped 6 times with the validation error of "How it works", and each new run continued.
- The group then held 478 episodes, 595 entities, and 1,769 facts. Each entity has one of the seven types.
- 327 facts have an end date. The repeal step ended 309 of them. Graphiti ended the other 18 through a new fact that contradicts the old one, for example the civil penalty of 500 penalty units of section 91, which ends on 26 November 2026.
- 295 of the 1,110 facts of the 2021 Act have an end date. Each of the 87 facts of the Act that name the basic online safety expectations ends on 25 November 2027. 65 of the 71 facts that name an industry code or an industry standard end on that day. The other 6 are also stated by a section that stays.
- 215 new facts start on 26 November 2026, and 443 start on 25 November 2027. One new fact has no start date.

An example. The question: "What happens to the basic online safety expectations under the Digital Duty of Care Bill, and from which date? What duty replaces them?" The answer, shortened: the Bill repeals the expectations from 25 November 2027, and this date is a placeholder because the Bill has no Royal Assent (Online Safety Act 2021 s 45 (repealed by the Digital Duty of Care Bill 2026, Sch 2 item 13)). A digital duty of care replaces them: a person responsible for an online service must take appropriate action to ensure a safe online environment (Online Safety Act 2021 s 26 (inserted by the Digital Duty of Care Bill 2026, Sch 2 item 8)).

Limits:

- For a change of words, the episode holds the heading of the section and the words of the change. It does not hold the old text of the section. A model decides which old fact ends. In the trial, the new penalty of section 91 ended the old one. The change of "48 hours" to "24 hours" in section 88 ended no old fact, so the graph holds both.
- The repeal step ends a fact only if each of its sources is repealed. This is safe, but a fact of a repealed section that another section restates stays open.
- A reader of the graph must use the dates. A search returns the ended facts too, and the instructions of the agent tell it to give the end date.

## How it works

- **Split.** `ActSplitter` reads the `.docx` file with `System.IO.Compression` and `System.Xml.Linq`. A section starts at a paragraph of the style `ActHead5`. The styles `ActHead2`, `ActHead3`, and `ActHead4` are the Part, the Division, and the Subdivision. This Act has no Chapter.
- **Provenance.** The place of a rule in the Act is provenance. It is not part of the ontology, and the model never reads it. Upstream Graphiti 0.32.0 has no way to store free metadata on an episode. So each episode carries its place in two stored fields:
  - `Name`: the section id, for example `Online Safety Act 2021 s 36`.
  - `SourceDescription`: the chain in one line, for example `Online Safety Act 2021 (Cth) > Part 3 > Division 4 > Section 36`. A level that does not exist is left out.

  The `Content` of an episode holds a topic line, the heading of the section, and its text. It has no chain and no section number. Each fact holds the uuids of the episodes that state it, so the tool finds the section id and the chain of each fact.

  The ingestion also writes `data/sections.<group id>.json`, one file for each group id. It holds one record for each episode, with the episode uuid. A run with `--limit` or `--resume` changes only the records of the sections that it processes, and never drops another record.
- **Reference time.** Each episode of the Act has the date of assent of the Act as its reference time: 23 July 2021. Each episode of the Bill has the day on which its Schedule commences.
- **Structured output.** Graphiti asks the model for JSON and puts the schema in the prompt text, and a model can copy the wrong shape from that text. The class `StructuredOutputChatClient` reads the schema of each Graphiti call from `GraphitiModelCall.Current` and sends it to OpenAI as native structured output. The section "Call information for host middleware and for the judge" of `docs/design-docs/public-surface-design.md` (decision D17) gives the host this extension point.
- **The ingestion can fail with a validation error.** The class does not ask for strict mode, because OpenAI rejects the schemas of Graphiti in strict mode (they have `$defs` and optional properties). In the non-strict mode the model still sometimes returns the wrong shape. Then Graphiti stops with `ValidationException: ... EdgeDuplicate`. In the trial runs of five sections this happened in 1 of 17 runs with the class and in 6 of 14 runs without it. The full run of the Act (about 1,100 calls of this kind) failed 2 times. Run the command again with `--resume`: it skips the finished episodes and processes an unfinished episode again.
- **Agent.** One agent of Microsoft Agent Framework. It turns the reasoning effort of the model off, because `gpt-6-luna` does not accept function tools together with reasoning in the Chat Completions API. It has one tool, `search_legislation(query)`. The tool calls the graph search of `IGraphiti` for the group id. The result has two lists. The list `facts` has, for each fact, the fact text, the uuid, the name of the relation, and the attributes of the relation (for example `penalty_units`). It has the source entity and the target entity, each with its uuid, its name, and its labels. A label is the entity type of the ontology. It also has the validity dates, the section id, and the chain. The tool reads the names of the entities with `Nodes.Entity.GetByUuidsAsync`. The list `sections` has the text of the sections that state these facts, and the text of sections that the search found by their own text (at most 6 sections, no section two times, each cut to 2,000 characters). The tool uses `AdvancedSearchAsync` with the recipe `CombinedHybridSearchRrf` for edges and episodes, so a section with no fact can still be found. The tool reads this text from the graph, because Graphiti stores the content of each episode. The instructions of the agent say: answer only from the tool results, quote or restate the words of the section, cite the section id for each statement, and say so if the graph has no answer. The agent must cite only a section id that is in the tool results of this conversation. The instructions also say that the law changes over time: a fact with an `invalid_at` date stops on that date, a fact of the Bill starts on its `valid_at` date, and the dates of the Bill are placeholders until the Royal Assent. A small model can still break this rule.

About 12 percent of the episodes (30 of 250) have more than 2,000 characters. The tool cuts their text, so the agent can miss the end of such a section. In a group from before the ontology, the text of each section still starts with the chain line.
- **Page.** A Next.js app with CopilotKit. The route `/api/copilotkit` forwards each chat request to the AG-UI server with an `HttpAgent`.
- **Tool call rendering.** The AG-UI server of Agent Framework sends the events of each tool call, and the result is in the event `TOOL_CALL_RESULT`. The page registers a renderer for the tool with the hook `useRenderTool` of `@copilotkit/react-core/v2` (`web/app/page.tsx`). CopilotKit draws the renderer again as the call changes, with the arguments so far, and then with the status `complete` and the result text. For a tool of a backend agent, the status does not show the event `TOOL_CALL_END`. So the page also subscribes to the agent (`useAgent` and `agent.subscribe`) and records the four events of each call as they arrive. The server sends the JSON text of the tool as a JSON string, so `web/app/result.ts` parses the text two times. It then checks each field with a Zod schema. If the text is not a result of the tool, the page shows the text as it is.
- **Graph.** `web/app/graph.tsx` draws the graph as SVG, and `web/app/result.ts` computes the place of each entity with a small spring model. The page uses no graph library. The same result always gives the same picture.
- **Design.** `web/PRODUCT.md` holds the product facts of the page, and `web/DESIGN.md` holds its visual rules. The folder `web/.impeccable/` holds the records of the design skill that made them.

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
- The page sends a Content Security Policy (`web/next.config.ts`). The browser then loads no image, font, media file, or frame from another host, and the page makes no request to another host. An answer of the model with the address of an image loads nothing.
- The text of each episode and each question goes to the OpenAI API. The key never goes to the page.

## Attribution

The text of the Online Safety Amendment (Digital Duty of Care) Bill 2026 comes from the Parliament of Australia, as passed by both Houses. This example did not check the licence of that text, and the repository holds none of it: you download the file yourself.

The text of the Online Safety Act 2021 (Cth) comes from the Federal Register of Legislation (<https://www.legislation.gov.au/>). It is published under the Creative Commons Attribution 4.0 International licence (<https://creativecommons.org/licenses/by/4.0/>). See the terms of use at <https://www.legislation.gov.au/terms-of-use>.

This example changes the text. It splits the text into sections, and it puts a topic line and the heading of each section before its text, without the section number. The topic line is the heading text of the Part and the Division. It also turns each tab into one space and keeps each hyphen (a non-breaking hyphen of the Word file becomes a plain hyphen, for example in "end-user" and "cyber-abuse"). It changes no word. The Commonwealth Coat of Arms is not part of the licence. This example does not include it.
