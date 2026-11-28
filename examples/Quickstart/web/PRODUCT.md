# Product

<!-- impeccable:product-schema 1 -->

This file holds the product facts of the web page of the interactive quickstart. It does not describe the repository. The maintainer confirmed these facts on 2026-11-28.

## Platform

web

## Users

The main reader is a .NET developer who evaluates Graphiti.Net. The developer runs the example on a local machine, asks one or two questions, and wants to see what the library returns.

## Product Purpose

The page is the front of the interactive quickstart. The reader asks a question about the Online Safety Act 2021 (Cth). An agent searches a Graphiti.Net knowledge graph and answers from the result.

The page is a success if, after one question, the reader sees that each fact is an edge between two entities, with validity dates and a source section. The temporal graph is the product. The answer text is the smaller part.

## Positioning

Each fact in the graph has a time range and a source. The page shows the result of the search tool as that graph, not only as the text of an answer.

## Operating Context

- The page runs on the loopback address of the developer. It is not on a network.
- A .NET server holds the agent and its one tool, `search_legislation(query)`. The page gets AG-UI events from it through the CopilotKit runtime route.
- The data is one Act: 250 episodes (one for each section), 383 entities, and 606 facts in the last full ingestion.

## Capabilities and Constraints

- The page shows each tool call and its result as the AG-UI events arrive: the start, the arguments, and the result.
- A result holds facts and section texts. Each fact has a source entity, a target entity, a relation name, a valid-from date, an optional invalid-from date, and the sections that state it.
- The page sends no telemetry and uses no CopilotKit cloud key.
- The page loads nothing from another host. It has no web fonts from a font service.
- A new npm package is a library choice. The maintainer decides it.
- Documents follow the Simple English rules of the repository.

## Evidence on Hand

- Real data: the graph of the Online Safety Act 2021 in the local Neo4j.
- The text of the Act comes from the Federal Register of Legislation, licence CC BY 4.0. The page must keep this attribution.
- The page has no logo and no brand assets. Do not invent them. The Commonwealth Coat of Arms is not part of the licence, and the page must not use it.

## Product Principles

1. The graph comes first. The answer text supports it.
2. Show what the library returned. Do not hide a field of the result, and do not invent one.
3. Each fact shows its dates and its source section.
4. The data must stay easy to read. Decoration and motion must never make a fact or a date harder to read.
5. The page must not look like a generic AI chat, a legal or government site, or a dark developer console.
