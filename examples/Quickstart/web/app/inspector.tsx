"use client";

import { useMemo, useState } from "react";
import { TemporalGraph, validity } from "./graph";
import { lineOf, linesOf, parseResult, sourceParts, sourceText, statedIn, timeFraction, type Fact, type SearchResult } from "./result";
import { eventsOf, useStore, type Focus, type ToolCall } from "./store";

// The date of assent of the Act. The time axis starts here if no fact has an earlier date.
const Assent = "2021-07-23";

function lineStyle(lines: string[], line: string): React.CSSProperties {
  const at = lines.indexOf(line);
  return { "--c": at < 0 ? "var(--ink)" : `var(--line-${at % 10})` } as React.CSSProperties;
}

function same(a: Focus, b: Focus): boolean {
  return a !== null && b !== null && a.kind === b.kind && a.id === b.id;
}

/** The AG-UI events of one tool call, in the order that the server sends them. */
function Events({ call }: { call: ToolCall }) {
  const { events } = useStore();
  const seen = eventsOf(events, call);
  const list = [
    { name: "TOOL_CALL_START", done: seen.start },
    { name: "TOOL_CALL_ARGS", done: seen.args },
    { name: "TOOL_CALL_END", done: seen.end },
    { name: "TOOL_CALL_RESULT", done: seen.result },
  ];
  const next = list.findIndex((event) => !event.done);
  return (
    <ol className="events" aria-label="AG-UI events of this tool call">
      {list.map((event, i) => (
        <li key={event.name} data-state={event.done ? "done" : i === next ? "now" : "wait"}>
          <code>{event.name}</code>
          <span>{event.done ? "received" : i === next ? "next" : "not yet"}</span>
        </li>
      ))}
    </ol>
  );
}

function Empty() {
  return (
    <div className="empty">
      <h2>The result of each search shows here</h2>
      <p>Ask a question on the left. The agent calls the tool <code>search_legislation</code>, and this area draws what the graph search of Graphiti.Net returned.</p>
      <dl>
        <dt>
          <svg width="56" height="16" aria-hidden="true">
            <circle cx="8" cy="8" r="6" className="stop" />
          </svg>
        </dt>
        <dd>An entity of the graph.</dd>
        <dt>
          <svg width="56" height="16" aria-hidden="true">
            <path d="M2 8H54" className="run" style={{ stroke: "var(--line-0)" }} />
          </svg>
        </dt>
        <dd>A fact between two entities. The colour is the section of the Act that states the fact.</dd>
        <dt>
          <svg width="56" height="16" aria-hidden="true">
            <path d="M2 8H54" className="run" style={{ stroke: "var(--line-1)", strokeDasharray: "9 6" }} />
          </svg>
        </dt>
        <dd>A fact with an end date. It is no longer valid.</dd>
      </dl>
    </div>
  );
}

function Key({ result, lines, focus }: { result: SearchResult; lines: string[]; focus: Focus }) {
  const { setHover, togglePin, pinned } = useStore();
  return (
    <ul className="key" aria-label="Sections that state the facts">
      {lines.map((line) => {
        const target: Focus = { kind: "line", id: line };
        const count = result.facts.filter((fact) => statedIn(fact, line)).length;
        return (
          <li key={line}>
            <button
              type="button"
              style={lineStyle(lines, line)}
              aria-pressed={same(pinned, target)}
              data-dim={focus !== null && !(focus.kind === "line" && focus.id === line)}
              onMouseEnter={() => setHover(target)}
              onMouseLeave={() => setHover(null)}
              onFocus={() => setHover(target)}
              onBlur={() => setHover(null)}
              onClick={() => togglePin(target)}
            >
              <i aria-hidden="true" />
              <Source ids={[line]} />
              <span>
                {count} {count === 1 ? "fact" : "facts"}
              </span>
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/** The share of the axis that stays free after the last date, so a fact that starts there has room for its line. */
const AxisRoom = 0.12;

function axisOf(result: SearchResult) {
  const today = new Date().toISOString().slice(0, 10);
  const dates = result.facts.flatMap((fact) => [fact.valid_at, fact.invalid_at]).filter((date): date is string => date !== null);
  const start = dates.reduce((min, date) => (date < min ? date : min), Assent);
  // A fact can have a date after today. The last date is then that date, and today is inside the axis.
  const last = dates.reduce((max, date) => (date > max ? date : max), today);
  const span = Math.max(Date.parse(last) - Date.parse(start), 86_400_000);
  const end = new Date(Date.parse(last) + (span * AxisRoom) / (1 - AxisRoom)).toISOString().slice(0, 10);
  const years: number[] = [];
  for (let year = Number(start.slice(0, 4)) + 1; year <= Number(end.slice(0, 4)); year++) {
    years.push(year);
  }

  // The labels of the axis: today first, then the start, then each other date of the result. A label that would sit on
  // a label before it is left out. The date column of each row still has each date.
  const marks: { date: string; at: number; now: boolean }[] = [{ date: today, at: timeFraction(today, start, end), now: true }];
  for (const date of [start, ...[...new Set(dates)].sort()]) {
    const at = timeFraction(date, start, end);
    // The label of today has a row of its own, so a date is compared only with the other dates.
    if (marks.every((mark) => mark.now || Math.abs(mark.at - at) >= 0.3)) {
      marks.push({ date, at, now: false });
    }
  }

  return { today, start, end, last, years, marks };
}

/** The state of a fact today. The dates of the fact and the date of today decide it. */
function stateOf(fact: Fact, today: string): string {
  if (fact.invalid_at !== null && fact.invalid_at <= today) {
    return "ended";
  }

  return fact.valid_at !== null && fact.valid_at > today ? "starts later" : "valid now";
}

function isLit(fact: Fact, focus: Focus): boolean {
  return !focus || (focus.kind === "line" ? statedIn(fact, focus.id) : fact.uuid === focus.id);
}

function sectionsOf(fact: Fact): string {
  return sourceText(fact.sources.map((source) => source.section_id));
}

/**
 * The sources of a fact. Each section shows one time. A change that the Bill makes to the section is a note in a
 * quieter style, so a long label wraps and never runs over the next column.
 */
function Source({ ids }: { ids: string[] }) {
  const parts = sourceParts(ids);
  if (parts.length === 0) {
    return <span className="source">unknown</span>;
  }

  return (
    <span className="source">
      {parts.map((part, i) => (
        <span key={part.section}>
          {i > 0 && ", "}
          {part.section}
          {part.notes.map((note) => (
            <small key={note}>{note}</small>
          ))}
        </span>
      ))}
    </span>
  );
}

/**
 * The validity ruler: one row for each fact on one time axis. The first column names the fact: its two entities, and
 * the relation under them. The axis runs from the first date to a point after the last date, and a line marks today.
 * A fact starts at a stop. The part of its line before today is solid, and the part after today is hollow. A fact with
 * an end date stops at a bar, and a fact with no end date runs to the edge and ends with an arrow.
 */
function Ruler({ result, lines, focus }: { result: SearchResult; lines: string[]; focus: Focus }) {
  const { setHover, togglePin, pinned } = useStore();
  const { today, start, end, last, years, marks } = axisOf(result);
  const now = timeFraction(today, start, end);
  return (
    <div className="ruler">
      <div className="ruler-axis" aria-hidden="true">
        <span>Fact</span>
        <span className="axis">
          {marks.map((mark) => (
            <span key={mark.date + String(mark.now)} data-now={mark.now} data-end={mark.at > 0.8} style={{ left: `${mark.at * 100}%` }}>
              {mark.now ? "Now" : mark.date}
            </span>
          ))}
        </span>
        <span>Valid from → to</span>
      </div>
      <ul aria-label={`Validity of each fact from ${start} to ${last}. Today is ${today}.`}>
        {result.facts.map((fact) => {
          const target: Focus = { kind: "fact", id: fact.uuid };
          const from = fact.valid_at ? timeFraction(fact.valid_at, start, end) : 0;
          const to = timeFraction(fact.invalid_at, start, end);
          const past = to > from ? Math.min(1, Math.max(0, (now - from) / (to - from))) : now >= to ? 1 : 0;
          const state = stateOf(fact, today);
          return (
            <li key={fact.uuid} style={lineStyle(lines, lineOf(fact))} data-dim={!isLit(fact, focus)}>
              <button
                type="button"
                aria-pressed={same(pinned, target)}
                aria-label={`${fact.source.name}, ${fact.name}, ${fact.target.name}. ${validity(fact.valid_at, fact.invalid_at)}. ${state}.`}
                onMouseEnter={() => setHover(target)}
                onMouseLeave={() => setHover(null)}
                onFocus={() => setHover(target)}
                onBlur={() => setHover(null)}
                onClick={() => togglePin(target)}
              >
                <span className="ruler-fact" title={`${fact.source.name} → ${fact.name} → ${fact.target.name}`}>
                  <span className="ruler-ends">
                    {fact.source.name} <i aria-hidden="true">→</i> {fact.target.name}
                  </span>
                  <span className="ruler-name">{fact.name}</span>
                </span>
                <span className="track" aria-hidden="true">
                  {years.map((year) => (
                    <b key={year} style={{ left: `${timeFraction(`${year}-01-01`, start, end) * 100}%` }} />
                  ))}
                  <span
                    className="bar"
                    data-open={fact.invalid_at === null}
                    data-nostart={fact.valid_at === null}
                    style={{ left: `${from * 100}%`, right: `${(1 - to) * 100}%`, ["--past" as string]: `${past * 100}%` }}
                  />
                </span>
                <span className="ruler-when">
                  <span className="date">{validity(fact.valid_at, fact.invalid_at)}</span>
                  <span className="ruler-state">{state}</span>
                </span>
              </button>
            </li>
          );
        })}
        <li className="ruler-now" role="presentation" aria-hidden="true">
          <span>
            <i style={{ left: `${now * 100}%` }} />
          </span>
        </li>
      </ul>
      <p className="ruler-note">
        {[
          { text: "before today", past: "100%", end: "none" },
          { text: "after today", past: "0%", end: "none" },
          { text: "an end date", past: "100%", end: "closed" },
          { text: "no end date", past: "100%", end: "open" },
        ].map((shape) => (
          <span key={shape.text}>
            <span className="track" aria-hidden="true">
              <span className="bar" data-nostart="true" data-end={shape.end} data-open={shape.end === "open"} style={{ left: 0, right: 0, ["--past" as string]: shape.past }} />
            </span>
            {shape.text}
          </span>
        ))}
      </p>
    </div>
  );
}

/** The fact that the reader points at, in full: the two entities, the relation, the dates, the sections, and the text. */
function Detail({ result, lines, focus }: { result: SearchResult; lines: string[]; focus: Focus }) {
  const fact = focus?.kind === "fact" ? result.facts.find((f) => f.uuid === focus.id) : undefined;
  if (!fact) {
    return (
      <p className="detail" data-empty="true">
        Point at a line, or select it, to read the fact.
      </p>
    );
  }

  // The box has a fixed height, so a new fact never moves the ruler or the table under the pointer. A long name or a
  // long fact text is cut here. The fact table below has the full text.
  const title = `${fact.source.name} → ${fact.name} → ${fact.target.name}`;
  return (
    <p className="detail" style={lineStyle(lines, lineOf(fact))} aria-live="polite">
      <strong title={title}>
        <i aria-hidden="true" />
        {title}
      </strong>
      <span className="detail-meta">
        <span className="date">{validity(fact.valid_at, fact.invalid_at)}</span> Stated in {sectionsOf(fact)}
      </span>
      <span className="detail-text" title={fact.fact}>
        {fact.fact}
      </span>
    </p>
  );
}

/**
 * The facts in the order of the result, with the facts of the pinned selection first. Only a pinned selection changes
 * the order. A hover never does, so no row moves under the pointer.
 */
function hoist<T>(items: T[], first: (item: T) => boolean): T[] {
  return [...items.filter(first), ...items.filter((item) => !first(item))];
}

function Facts({ result, lines, focus }: { result: SearchResult; lines: string[]; focus: Focus }) {
  const { setHover, togglePin, pinned } = useStore();
  // A fact that the reader pins in this table stays in its place. The row under the pointer must not move away.
  // The store keeps the object of a pin, so a pin from the graph or the ruler is another object, and it moves the row.
  const [pinnedHere, setPinnedHere] = useState<Focus>(null);
  const moved = pinned !== null && pinned !== pinnedHere;
  const facts = moved ? hoist(result.facts, (fact) => isLit(fact, pinned)) : result.facts;
  return (
    <div className="scroll">
      <table className="facts">
        <thead>
          <tr>
            <th scope="col">Stated in</th>
            <th scope="col">From</th>
            <th scope="col">Relation</th>
            <th scope="col">To</th>
            <th scope="col">Valid from</th>
            <th scope="col">Invalid from</th>
          </tr>
        </thead>
        {facts.map((fact) => {
          const target: Focus = { kind: "fact", id: fact.uuid };
          return (
            <tbody
              key={fact.uuid}
              style={lineStyle(lines, lineOf(fact))}
              data-dim={!isLit(fact, focus)}
              data-pinned={same(pinned, target)}
              onMouseEnter={() => setHover(target)}
              onMouseLeave={() => setHover(null)}
            >
              <tr>
                <td className="line">
                  <i aria-hidden="true" />
                  <Source ids={fact.sources.map((source) => source.section_id)} />
                </td>
                <td>{fact.source.name}</td>
                <td>
                  <button
                    type="button"
                    aria-pressed={same(pinned, target)}
                    onFocus={() => setHover(target)}
                    onBlur={() => setHover(null)}
                    onClick={() => {
                      setPinnedHere(target);
                      togglePin(target);
                    }}
                  >
                    {fact.name.split("_").map((part, i) => (
                      <span key={i}>
                        {i > 0 && "_"}
                        {i > 0 && <wbr />}
                        {part}
                      </span>
                    ))}
                  </button>
                </td>
                <td>{fact.target.name}</td>
                <td className="date">{fact.valid_at ?? "none"}</td>
                <td className="date">{fact.invalid_at ?? "none"}</td>
              </tr>
              <tr className="sentence">
                <td />
                <td colSpan={5}>{fact.fact}</td>
              </tr>
            </tbody>
          );
        })}
      </table>
    </div>
  );
}

function Sections({ result, lines, focus }: { result: SearchResult; lines: string[]; focus: Focus }) {
  const { setHover, pinned } = useStore();
  // The sections of the pinned selection come first: the sections that state the pinned fact, or the pinned section.
  const pinnedFact = pinned?.kind === "fact" ? result.facts.find((f) => f.uuid === pinned.id) : undefined;
  const first = (sectionId: string) =>
    pinned !== null && (pinned.kind === "line" ? pinned.id === sectionId : pinnedFact !== undefined && statedIn(pinnedFact, sectionId));
  const sections = hoist(
    result.sections.map((section, i) => ({ section, i })),
    ({ section }) => first(section.section_id),
  );
  const focusFact = focus?.kind === "fact" ? result.facts.find((f) => f.uuid === focus.id) : undefined;
  const lit = (sectionId: string) => !focus || (focus.kind === "line" ? focus.id === sectionId : focusFact !== undefined && statedIn(focusFact, sectionId));
  return (
    <div className="sections">
      {sections.map(({ section, i }) => {
        const stated = lines.includes(section.section_id);
        return (
          <details
            // Section 5 of the Act is five episodes with the same section id, so the key also has the position.
            key={`${section.section_id}#${i}`}
            open={i < 2 || first(section.section_id)}
            style={lineStyle(lines, section.section_id)}
            data-dim={!lit(section.section_id)}
            onMouseEnter={() => stated && setHover({ kind: "line", id: section.section_id })}
            onMouseLeave={() => setHover(null)}
          >
            <summary>
              <i aria-hidden="true" data-plain={!stated} />
              <strong>
                <Source ids={[section.section_id]} />
              </strong>
              <span>{section.chain}</span>
              {!stated && <em>found by its text</em>}
            </summary>
            <p>{section.text}</p>
          </details>
        );
      })}
    </div>
  );
}

function Result({ call, result }: { call: ToolCall; result: SearchResult }) {
  const { hover, pinned, setHover, togglePin } = useStore();
  const focus = hover ?? pinned;
  const lines = useMemo(() => linesOf(result), [result]);
  const entities = useMemo(() => new Set(result.facts.flatMap((fact) => [fact.source.uuid, fact.target.uuid])).size, [result]);
  return (
    <>
      <Calls />
      <header className="head">
        <h2>
          {result.facts.length} {result.facts.length === 1 ? "fact" : "facts"} between {entities} {entities === 1 ? "entity" : "entities"}
        </h2>
        <p>
          Search: <q>{result.query || call.query}</q>
        </p>
      </header>
      <Events call={call} />
      {result.facts.length === 0 ? (
        <p className="note">The search found no fact for this query. {result.sections.length > 0 ? "It found the sections below by their text." : "It found no section."}</p>
      ) : (
        <>
          <TemporalGraph result={result} lines={lines} focus={focus} pinned={pinned} onHover={setHover} onPin={togglePin} />
          <Detail result={result} lines={lines} focus={focus} />
          <Ruler result={result} lines={lines} focus={focus} />
          <Key result={result} lines={lines} focus={focus} />
          <h3>Facts</h3>
          <Facts result={result} lines={lines} focus={focus} />
        </>
      )}
      {result.sections.length > 0 && (
        <>
          <h3>Section texts</h3>
          <Sections result={result} lines={lines} focus={focus} />
        </>
      )}
      <details className="raw">
        <summary>Result text of the tool call (JSON)</summary>
        <pre>{JSON.stringify(result, null, 2)}</pre>
      </details>
    </>
  );
}

/** One button for each tool call of the conversation, so that the reader can change the call from the result area. */
function Calls() {
  const { calls, selectedId, select } = useStore();
  if (calls.length < 2) {
    return null;
  }

  return (
    <nav className="calls" aria-label="Tool calls of this conversation">
      {calls.map((call, i) => (
        <button key={call.id} type="button" aria-pressed={call.id === selectedId} title={call.query} onClick={() => select(call.id)}>
          Search {i + 1}
        </button>
      ))}
    </nav>
  );
}

/** The result area: the selected tool call, from its first AG-UI event to its result. */
export function Inspector() {
  const { calls, selectedId, events } = useStore();
  const call = calls.find((c) => c.id === selectedId) ?? calls[calls.length - 1];
  const result = useMemo(() => (call?.status === "complete" ? parseResult(call.result) : null), [call?.status, call?.result]);

  if (!call) {
    return <Empty />;
  }

  const running = call ? eventsOf(events, call).end : false;
  if (call.status !== "complete") {
    return (
      <>
        <Calls />
        <header className="head">
          <h2>{running ? "The search runs on the graph" : "The agent writes the search"}</h2>
          <p>
            Search: <q>{call.query || "…"}</q>
          </p>
        </header>
        <Events call={call} />
        <div className="graph waiting" aria-hidden="true" />
      </>
    );
  }

  if (!result) {
    return (
      <>
        <Calls />
        <header className="head">
          <h2>The page cannot read this result</h2>
          <p>The result text of the tool is not the JSON that the page expects. The text is below.</p>
        </header>
        <Events call={call} />
        <pre className="rawtext">{call.result}</pre>
      </>
    );
  }

  return <Result call={call} result={result} />;
}
