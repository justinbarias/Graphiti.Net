---
name: "Graphiti.Net Quickstart"
description: "The visual system of the implemented graph search page."
colors:
  ground: "#f2f5f3"
  sheet: "#fbfcfb"
  ink: "#141a33"
  ink-2: "#454c66"
  rule: "#c5cdc9"
  rail: "#1b2757"
  rail-2: "#2a3875"
  rail-3: "#34448a"
  rail-rule: "#5d6bb0"
  rail-ink: "#eef0fa"
  rail-ink-2: "#bcc4e4"
  line-0: "#d8432a"
  line-1: "#0f7f7a"
  line-2: "#c98a00"
  line-3: "#6a3fb0"
  line-4: "#2f8a3c"
  line-5: "#c2378f"
  line-6: "#7a5230"
  line-7: "#3d6fd6"
  line-8: "#5b6470"
  line-9: "#8f9412"
typography:
  body:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.5
  rail-title:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "1.6rem"
    lineHeight: 1.15
    letterSpacing: "-0.01em"
  result-title:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "1.75rem"
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  result-title-narrow:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "1.4rem"
    lineHeight: 1.2
    letterSpacing: "-0.01em"
  section-title:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "1.05rem"
  data:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "0.87rem"
  label:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "0.8rem"
  tool-name:
    fontFamily: "ui-monospace, \"SF Mono\", Menlo, Consolas, monospace"
    fontSize: "0.8rem"
    fontWeight: 600
  code:
    fontFamily: "ui-monospace, \"SF Mono\", Menlo, Consolas, monospace"
    fontSize: "0.87em"
  entity:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "13px"
    fontWeight: 600
  relation:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "11px"
    fontWeight: 600
    letterSpacing: "0.02em"
  graph-date:
    fontFamily: "ui-monospace, \"SF Mono\", Menlo, Consolas, monospace"
    fontSize: "10.5px"
    fontWeight: 400
    letterSpacing: "0"
  table-header:
    fontFamily: "system-ui, -apple-system, \"Segoe UI\", sans-serif"
    fontSize: "0.87rem"
    fontWeight: 700
    letterSpacing: "0.04em"
rounded:
  control: "3px"
  conversation: "16px"
  capsule: "26px"
  reader-message: "18px 18px 4px 18px"
  line-mark: "2px"
  stop: "50%"
spacing:
  px-2: "2px"
  px-4: "4px"
  px-6: "6px"
  px-7: "7px"
  px-8: "8px"
  px-9: "9px"
  px-10: "10px"
  px-12: "12px"
  px-14: "14px"
  px-16: "16px"
  px-20: "20px"
  px-24: "24px"
  px-32: "32px"
  px-36: "36px"
  px-48: "48px"
components:
  rail:
    backgroundColor: "{colors.rail}"
    textColor: "{colors.rail-ink}"
  question-input:
    backgroundColor: "{colors.rail-2}"
    rounded: "{rounded.capsule}"
  tool-call-row:
    backgroundColor: "{colors.rail-2}"
    textColor: "{colors.rail-ink}"
    rounded: "{rounded.conversation}"
    padding: "10px 14px 10px 38px"
    width: "100%"
  tool-call-row-hover:
    backgroundColor: "{colors.rail-3}"
  tool-call-row-selected:
    backgroundColor: "{colors.ground}"
    textColor: "{colors.ink}"
  event-list:
    textColor: "{colors.ink-2}"
    typography: "{typography.label}"
  call-button:
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "3px 12px"
  call-button-selected:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.ground}"
  graph:
    backgroundColor: "{colors.sheet}"
    height: "470px"
  graph-narrow:
    height: "540px"
  fact-detail:
    backgroundColor: "{colors.sheet}"
    typography: "{typography.data}"
    padding: "8px 12px"
  validity-ruler:
    typography: "{typography.label}"
  section-key:
    backgroundColor: "{colors.sheet}"
    textColor: "{colors.ink}"
    rounded: "{rounded.control}"
    padding: "4px 10px 4px 8px"
  section-key-selected:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.ground}"
  fact-table:
    typography: "{typography.data}"
    width: "100%"
  section-texts:
    textColor: "{colors.ink}"
---

# Design System: Graphiti.Net Quickstart

## Overview

**Creative North Star: "Working Timetable"**

The page pairs a blue conversation rail with a light result area. Lines connect entities and carry the colors of their source sections.
Dates, rules, and compact rows organize the result. The graph, ruler, table, and section texts share one focus state.

This record describes the current source in [globals.css](app/globals.css), [page.tsx](app/page.tsx), [inspector.tsx](app/inspector.tsx), [graph.tsx](app/graph.tsx), and [tool-call.tsx](app/tool-call.tsx).
[store.tsx](app/store.tsx) and [result.ts](app/result.ts) define selection, section order, and graph data.
[PRODUCT.md](PRODUCT.md) supplies the product context. This record covers this page only.

**Key Characteristics:**

- A dark rail beside a light graph workspace.
- Source colors that connect facts to sections.
- Flat surfaces with thin borders.
- Compact data with distinct dates and relation names.
- Shared focus with short step transitions.

## Colors

The frontmatter preserves every color token from the stylesheet.

### Primary

| Token | Role |
| --- | --- |
| `rail` | The deep blue conversation background, native control accent, and text selection background. |
| `rail-2` | The lighter blue input surface, chat surfaces, and resting tool row background. |
| `rail-3` | The tool row hover background, disabled send background, and reader message surface. |
| `rail-rule` | The rail scrollbar thumb, chat borders, and translucent rules between example questions. |
| `rail-ink` | The main rail text, links, send background, and tool row focus outline. |
| `rail-ink-2` | The rail description, attribution, secondary chat text, input placeholder, focus border, arrows, and disabled send text. |

### Neutral

| Token | Role |
| --- | --- |
| `ground` | The workspace background, selected tool row, selected control text, and ruler stop interior. |
| `sheet` | The graph, fact detail, raw result, hover rows, and neutral graph stops. |
| `ink` | The main text, strong rules, stops, focus outlines, and selected control backgrounds. |
| `ink-2` | The secondary text, graph dates, fact sentences, and inactive event text. |
| `rule` | The light borders, table dividers, year marks, and resting relation underlines. |

### Source lines

| Token | Color name | Section assignment |
| --- | --- | --- |
| `line-0` | Vermilion | First section |
| `line-1` | Teal | Second section |
| `line-2` | Ochre | Third section |
| `line-3` | Violet | Fourth section |
| `line-4` | Green | Fifth section |
| `line-5` | Magenta | Sixth section |
| `line-6` | Brown | Seventh section |
| `line-7` | Blue | Eighth section |
| `line-8` | Slate | Ninth section |
| `line-9` | Olive | Tenth section |

**The Source Color Rule.** Each source section has one line color. These colors have no other job.
Status, validity, selection, and errors use text, shape, neutral contrast, or motion.

Section order comes from the result. First sources come first, then other sources that do not yet have a line.
Each fact takes the color of its first source. A fact without a source uses the `unknown` entry.
The key, graph, detail, ruler, table, and section marks share that assignment.
The empty-state lines illustrate source colors. Their solid and dashed shapes explain validity.

The code repeats the palette after ten sections. This limit permits color collisions and does not establish a rule for new surfaces.
Section names remain necessary because color alone does not identify every source.

## Typography

The frontmatter records the exact font stacks and sizes. The stylesheet loads no font file.
The sans stack serves the interface. The mono stack serves tool names, code, and dates.

| Role | Use |
| --- | --- |
| `rail-title` | The main question heading in the rail. |
| `result-title` | Result headings and the empty-state heading. |
| `result-title-narrow` | Result headings at the narrow page breakpoint. |
| `section-title` | The headings above facts and section texts. |
| `body` | General text and source paragraphs. |
| `data` | The table, fact detail, section key, call buttons, and rail description. |
| `label` | Event rows, ruler rows, and call status text. |
| `tool-name` | The tool name inside a call row. |
| `code` | Code and raw result text, relative to their parent size. |
| `entity` | Graph entity labels. |
| `relation` | Graph relation labels. |
| `graph-date` | Dates beneath graph relations. |
| `table-header` | Uppercase column headings. |

**The Table Rank Rule.** The fact table uses one font size. Weight, case, spacing, and rules establish rank.

Dates and counts use tabular numerals. Date text does not wrap.
Relation labels and ruler names use `0.02em` letter spacing. Table headings and the ruler title use `0.04em`.
The code sets no root font size. The body size does not change the meaning of `rem`.
Current headings use the system stack. This record does not establish that choice as a display-font rule for new surfaces.

## Layout

The desktop shell uses `minmax(340px, 400px) minmax(0, 1fr)` columns and a `100dvh` height.
The rail stacks its header, flexible chat, and footer. The result area scrolls vertically.

| Area | Padding or gap |
| --- | --- |
| Rail header | `24px 24px 12px` padding |
| Rail footer | `8px 24px 14px` padding |
| Result area | `24px 32px 48px` padding |
| Result header | `4px 24px` gap and `8px` bottom padding |
| Event list | `4px 20px` gap and `10px` top margin |
| Call buttons | `6px` gap and `14px` bottom margin |
| Graph and ruler | `12px` top margin |
| Section key | `6px 8px` gap and `10px` top margin |
| Section headings | `36px 0 8px` margin |
| Fact detail | Two rows of `1.5em` and one row of `3em` |
| Section summary | `2px 10px` gap and `9px 0` padding |

The spacing entries record literal stylesheet values. The page has no CSS spacing variables or uniform spacing formula.
Result queries stop at `60ch`, notes at `65ch`, and section paragraphs at `72ch`.
Fact sentences have a `75ch` maximum width. The empty area has a `62ch` maximum width and `8vh` top padding.
The empty legend uses two columns, a `10px 14px` gap, and `16px` top padding.

### Narrow page

At viewport widths of `899px` or less, the shell becomes a vertical block.
The rail comes first and has an `88dvh` height. The result follows with `20px 16px 40px` padding and visible overflow.
Result headings use the narrow title size. The empty area loses its top padding.
A click on a rail tool row scrolls to the result and moves keyboard focus there.

The ruler uses `minmax(0, 42%) minmax(0, 1fr)` columns. It hides the separate date column and keeps the time axis.
The names of a fact can take two lines there. The axis shows the label of today and the first date only.
The table loses its minimum width and header row. Each fact becomes a block with `10px 0` padding.
Cells wrap into rows with a `2px 10px` gap. Arrows separate the source, relation, and target.
Dates gain the labels `valid from` and `invalid from`. The fact sentence loses its empty first cell.

### Narrow graph

Graph width has a separate threshold of `700px`. A graph below that width has a `540px` height instead of `470px`.
Only the focused fact shows its relation label. Entity names have an 18-character limit instead of 30, with further clipping near edges.
For more than five facts, graph dates appear only on the focused fact. The ruler and table retain the dates.

## Elevation & Depth

The custom surfaces use no shadows. Background contrast, borders, and rules divide the page.
The graph and fact detail share the sheet background. The detail joins the graph without a top border.
The input explicitly removes its shadow.

**The Shared Focus Rule.** Hover takes priority over a pinned selection. Unrelated data dims but remains in the layout.

Graph edges, nodes, ruler rows, and table groups dim to `0.16` opacity.
Section texts and key buttons dim to `0.45` opacity.
These states use the `--step` transition: `90ms steps(2, end)`.
The tool row uses the same transition for background and text color.

The active event and tool row indicator use `tick 0.8s steps(2, end) infinite`.
At the animation midpoint, opacity becomes `0.2`.
Reduced motion disables these two animations. It does not disable the short step transitions.

## Shapes

The conversation uses soft shapes: capsules, circles, and a `16px` radius. The result area keeps the `3px` control radius.
The question input uses a `26px` capsule radius. The send button and tool stop marker use circles.
Reader messages use `18px 18px 4px 18px` corners, with one tight corner beside the speaker.
Example questions use open rows with square corners. Source marks use the line-mark radius.
Source marks measure `20px` by `5px`. Graph entities and event markers use circles.
Thin borders use `1px`. Result headers and table headers use `2px` ink rules.
Focus outlines use `2px` ink strokes with a `2px` offset. Tool rows use rail ink for that outline.
Graph edges replace the outline with a thicker line.

## Components

### Rail

The rail holds the conversation in a blue column. Its footer retains the Act attribution and license link.
CopilotKit surfaces inherit the rail palette. The welcome screen aligns content left and places it at the bottom with `0 24px 8px` padding.
Its heading uses `1.15rem`, weight 600, a `1.3` line height, and a `16px` bottom margin.
Example questions form open rows without boxes. Thin translucent rules separate them, and each row ends with an arrow.
A hover underlines the question and brightens the arrow. It does not change the padding.
Each question uses `11px 26px 11px 2px` padding, the data type size, and a `1.4` line height.
Hover moves the left padding to `8px` and brightens the arrow. The rows keep a clear background.
CopilotKit places the input block over the bottom of the message list. The block has the rail color and a `44px` fade above it, so a message leaves view before it meets the input.
The footer holds the attribution in one short sentence for the Act and one for the Bill.
The input uses the secondary rail surface and a capsule shape. Its transparent border becomes secondary rail ink on focus.
The textarea has `8px` left padding and a secondary rail ink placeholder.
The round send button uses rail ink over rail blue. Its disabled state uses `rail-3` with secondary rail ink text.
The reader message uses `rail-3` and soft corners with one tight corner at the lower right.
The welcome input aligns with the heading and example questions without extra horizontal wrapper padding.
The file menu button stays hidden because this page accepts no file.

### Tool call row

Each call forms a full-width button with the tool name, current state, and query.
The row uses a `16px` radius, no border, and `10px 14px 10px 38px` padding.
The resting row uses `rail-2`. Hover changes its background to `rail-3`.
The selected row uses the ground background and ink text. The query wraps at any character if necessary.
A `12px` round stop marker sits at the left with a `3px` current-color border.
The marker blinks during a running call. Selection fills it with ink.
The row reports argument arrival, search progress, result counts, or an unreadable result.
A new call selects itself. A change of call clears hover and the pinned selection.

### Event list

Four event entries show the start, arguments, end, and result of the selected call.
A `9px` outlined circle means an event has not arrived. A filled circle means it arrived.
The next event blinks. Text also marks each event as received, next, or not yet.
Completed event names use ink and weight 600. The list wraps across lines.

### Call buttons

The result area shows call navigation only after the conversation has two calls.
Buttons show numbered searches and expose their queries as titles. The row wraps.
The selected button uses ink fill and ground text. Other buttons retain an ink border and transparent background.
The stylesheet adds no distinct hover fill to these buttons. They use the shared focus outline.

### Graph

Entities appear as neutral stops. Facts appear as source-colored paths with small sheet-colored direction arrows.
Paths use a `4px` stroke and round ends. Ended facts use a `10 7` dash pattern.
A keyboard-focused or pinned edge uses a `7px` stroke. Its transparent hit path has an `18px` stroke.
Entity circles use a `3px` ink stroke. Their radius is `8px`, or `11px` for more than two connections.
Entity and relation labels use a `4px` sheet outline for separation from paths.
Parallel facts use different bends. Facts that connect an entity to itself form loops.

Hover or keyboard focus highlights a fact. Click, Enter, or Space toggles its pinned state.
A section focus highlights every fact that names that section, including secondary sources.
The graph dims unrelated entities and edges. It does not remove them.

### Fact detail

The detail area sits directly beneath the graph. It has a fixed height of `calc(6em + 18px)`.
The height never changes, so a new fact does not move the ruler or the table under the pointer.
Without a fact focus, it shows a short instruction in secondary ink.
A fact focus shows three rows: the fact, then its dates and sections, then its sentence.
The first two rows stay on one line and end with an ellipsis. The sentence shows two lines at most.
The fact table holds the full text. The source mark carries the line color, and dates use the mono stack.
A polite live region announces the selected fact text.

**The Still Hover Rule.** A hover never changes the size of an element. It changes color, opacity, or decoration only.

**The Pinned First Rule.** A pinned selection moves its rows to the top of the fact table and of the section texts. A hover never changes an order.
A pinned fact comes first in the fact table, and the sections that state it come first in the section texts, and they are open.
A pinned section brings its facts and its text to the top in the same way. The other rows follow in the order of the result, dimmed.
The rows of the ruler, the key, and the graph never change their order, so the element under the pointer does not move.
A fact that the reader pins in the fact table itself stays in its place there. Only the section texts change their order.
The order of the result returns when the reader clears the pin. The page adds no announcement: the fact detail is the one live region.

### Validity ruler

Each fact has one button row on a shared time axis.
Desktop columns use `minmax(190px, 34%) minmax(0, 1fr) 176px`, with `16px` gaps. Thin rules separate the rows.

The first column names the fact. Its first line holds the source entity, an arrow, and the target entity, in weight 600.
Its second line holds the relation name in secondary ink. A relation name alone never names a row.
Each line stays on one line and ends with an ellipsis. The full text is the title of the cell.
The third column holds the dates in the mono stack, and under them the state today: `valid now`, `ended`, or `starts later`.

The axis starts at the earlier of assent or the earliest fact date.
It ends after the later of today or the latest fact date: `12%` of the axis stays free after that date.
So a fact that starts at the last date has room for its line. Thin marks show calendar years.
The axis header has two rows. The upper row holds the label `NOW` at the date of today, in ink and weight 700.
The lower row holds the first date and each other date of the result that has room. A label near the right end hangs to the left of its date.
A `1px` ink line runs through each row at the date of today. It sits over the rows and takes no pointer.

The track measures `14px` high. The bar measures `6px` high and at least `6px` wide.
The part of a bar before today is solid in the source color. The part after today is hollow, with a `1.5px` outline in the source color.
A known start has a `10px` circular stop with a `2px` border. An unknown start has no stop.
A closed interval ends with a `14px` vertical mark and a `3px` border.
An open interval runs to the edge of the track and ends with an arrow.
Shape carries each difference: solid and hollow for before and after today, the mark and the arrow for an end date and no end date.
A note under the rows shows these four shapes in secondary ink with their words.
Hover and pinned rows use the sheet background. Hover, focus, and clicks share the graph selection behavior.
A row never changes its height on hover.

### Section key

Each source section has a button with a line mark, section name, and fact count.
The key wraps across rows. Hover changes the border from rule to ink.
A pinned button uses ink fill and ground text. Its line mark keeps the source color.
Hover or keyboard focus highlights matching facts. A click toggles the pinned section.

### Fact table

Each fact uses two rows inside one table body. The first row shows sources, entities, relation, and validity dates.
The second row shows the full fact sentence in secondary ink.
The desktop table uses fixed layout, full width, and an `880px` minimum width. Its wrapper permits horizontal scrolling.
The source column measures `168px`, and a source label wraps inside it. A change that the Bill makes to a section is a note under the section, in secondary ink. The relation column takes `24%`, and date columns measure `112px`. The two entity columns share the remaining width.
Header padding is `5px 16px 5px 0`. Cell padding is `7px 16px 0 0`.
Sentence padding is `2px 10px 8px 0`. Thin rules separate facts.

Hover and pinned facts use the sheet background. The relation button has a rule-colored underline at a `3px` offset.
A pinned relation changes the underline to ink. Long relations can wrap after underscores.
The source cell lists every source section. Its color mark follows the first source.
Missing dates show `none`.

### Section texts

Each section uses a native disclosure with thin rules. The first two sections start open.
The summary shows a source mark, section name, and source chain. Its text control changes between Show and Hide.
Sections without a matching fact use a hollow neutral mark and a text-search label.
The paragraphs preserve line breaks. Open paragraphs have a `16px` bottom margin.
Hover on a sourced section highlights matching facts. The summary retains native keyboard operation for disclosure.
The code does not connect keyboard focus on a summary to shared graph focus.

### Result states

Before the first call, the result area shows a short guide to entities, facts, and ended facts.
During argument arrival or search progress, a heading and event list explain the state.
The graph placeholder stays `470px` high. Vertical stripes repeat every `60px`, with a `1px` rule after `59px` of sheet.

A valid result shows counts and the query. A result with no facts shows a message instead of the graph, detail, ruler, key, and table.
Available section texts still appear. An unreadable result shows its raw text beneath an error heading and the event list.
No source color marks an error.

A disclosure holds the parsed result as JSON. Raw result blocks use `12px` padding, a rule border, and a `420px` maximum height.
These blocks scroll and wrap long text. The raw disclosure has a `36px` top margin.

## Do and Do Not

### Do

- Do keep each source color consistent across the graph, ruler, key, table, and section texts.
- Do use shape and text to explain validity and progress.
- Do preserve source names, dates, and full fact sentences.
- Do keep the page breakpoint separate from the graph width threshold.
- Do retain the Act attribution and license link.

### Do Not

- Do not use source colors for status, errors, or action emphasis.
- Do not hide unrelated facts during shared focus.
- Do not add shadows to these flat custom surfaces.
- Do not treat palette reuse after ten sections as a unique source key.
