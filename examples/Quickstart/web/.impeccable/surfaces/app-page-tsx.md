---
version: 1
slug: "app-page-tsx"
primary_target: "app/page.tsx"
related_targets: []
---

# Surface brief: the quickstart page

Scope: the one page of the interactive quickstart (`app/page.tsx`). Visitor mode: Operate.

Audience and job: a .NET developer who evaluates Graphiti.Net asks one or two questions and reads what the search tool returned. The temporal graph is the product. The answer text is the smaller part.

Constraints: the page loads nothing from another host, sends no telemetry, and adds no npm package. It must not look like a generic AI chat, a legal or government site, or a dark developer console. Decoration must never make a fact or a date harder to read.

## Direction contract

THESIS: Each fact is a service on a working timetable. It runs from a date, between two entities, on the line of the section that states it. The page refuses the centered chat column with result cards inside the messages.

OWN-WORLD: A cool white ground, a deep blue rail, and one line colour for each source section (vermilion, teal, ochre, violet, then green and magenta). A colour means one section and nothing else. Thin dark rules, tabular numerals for each date, and one type size in the data table, where weight, case, and rules carry the rank. A state change is a short step.

STORY: The reader asks a question. The rail shows each tool call as its events arrive. The reader sees the graph of the result, then reads the dates and the sections of each fact, and trusts the answer because each fact has a source.

FIRST VIEWPORT: Left, a blue rail of 400 px with the title, the conversation, each tool call, and the question input at the bottom. Right, the result of the selected tool call: a heading with the fact count, the graph at full width (about 470 px high) with the validity ruler under it, the key of section colours, then the fact table and the section texts.

SIGNATURE: Point at a section colour in the key, a fact, or a section text. The graph, the ruler, and the table then show only the facts of that section.

FORM: Working Timetable, position 3 of 7 on the ordered list. seed:b8e8e63f7b88cee0

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

Unresolved: nearly all facts of this Act start on 23 July 2021 and have no end date, so the ruler shows many equal lines.
