"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { layoutGraph, shortSection, statedIn, type GraphEdge, type SearchResult } from "./result";
import type { Focus } from "./store";

type Props = {
  result: SearchResult;
  lines: string[];
  focus: Focus;
  pinned: Focus;
  onHover: (focus: Focus) => void;
  onPin: (focus: Focus) => void;
};

function clip(text: string, limit: number): string {
  return text.length > limit ? `${text.slice(0, limit - 1)}…` : text;
}

/** "2021-07-23 →" for a fact with no end, "2021-07-23 → 2024-01-10" for a fact that ended. */
export function validity(validAt: string | null, invalidAt: string | null): string {
  return `${validAt ?? "no start date"} → ${invalidAt ?? ""}`.trim();
}

function geometry(edge: GraphEdge) {
  const { from, to, bend } = edge;
  if (from === to) {
    // A fact from an entity to the same entity: a loop above the entity. A second loop is larger, so no loop hides
    // another loop.
    const size = 1 + Math.abs(bend) / 34;
    const [w, h] = [46 * size, 70 * size];
    const path = `M${from.x - 10} ${from.y - 8} C${from.x - w} ${from.y - h} ${from.x + w} ${from.y - h} ${from.x + 10} ${from.y - 8}`;
    return { path, midX: from.x, midY: from.y - 8 - (h - 8) * 0.75, angle: 0, normalX: 0, normalY: -1 };
  }

  const dx = to.x - from.x;
  const dy = to.y - from.y;
  const length = Math.hypot(dx, dy);
  let normalX = -dy / length;
  let normalY = dx / length;
  const controlX = (from.x + to.x) / 2 + normalX * bend * 2;
  const controlY = (from.y + to.y) / 2 + normalY * bend * 2;
  // The label goes on the upper side of the line.
  if (normalY > 0) {
    normalX = -normalX;
    normalY = -normalY;
  }

  // The label sits nearer to the entity with fewer facts, away from the crowded center of a star.
  const t = from.degree > to.degree ? 0.64 : from.degree < to.degree ? 0.36 : 0.5;
  const at = (a: number, c: number, b: number) => (1 - t) * (1 - t) * a + 2 * t * (1 - t) * c + t * t * b;
  const tangentX = 2 * (1 - t) * (controlX - from.x) + 2 * t * (to.x - controlX);
  const tangentY = 2 * (1 - t) * (controlY - from.y) + 2 * t * (to.y - controlY);
  return {
    path: `M${from.x} ${from.y} Q${controlX} ${controlY} ${to.x} ${to.y}`,
    midX: at(from.x, controlX, to.x),
    midY: at(from.y, controlY, to.y),
    angle: (Math.atan2(tangentY, tangentX) * 180) / Math.PI,
    normalX,
    normalY,
  };
}

/**
 * The temporal graph of one tool result. An entity is a stop. A fact is a line from its source entity to its target
 * entity, in the colour of the section that states it. A fact with an end date is a broken line.
 */
export function TemporalGraph({ result, lines, focus, pinned, onHover, onPin }: Props) {
  const frame = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const element = frame.current;
    if (!element) {
      return;
    }

    const observer = new ResizeObserver(([entry]) => setWidth(Math.round(entry.contentRect.width)));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const narrow = width < 700;
  const height = narrow ? 540 : 470;
  const graph = useMemo(() => (width > 0 ? layoutGraph(result, width, height) : null), [result, width, height]);
  const lit = (edge: GraphEdge) => !focus || (focus.kind === "line" ? statedIn(edge.fact, focus.id) : edge.fact.uuid === focus.id);
  const nameLimit = narrow ? 18 : 30;

  return (
    <div className="graph" ref={frame} style={{ height }} data-narrow={narrow} data-crowded={result.facts.length > 5}>
      {graph && (
        <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="group" aria-label={`Graph of ${result.facts.length} facts between ${graph.nodes.length} entities`}>
          {graph.edges.map((edge) => {
            const g = geometry(edge);
            const on = lit(edge);
            const target: Focus = { kind: "fact", id: edge.fact.uuid };
            const label = `${edge.from.name}, ${edge.fact.name}, ${edge.to.name}. ${validity(edge.fact.valid_at, edge.fact.invalid_at)}. Stated in ${shortSection(edge.line)}.`;
            return (
              <g
                key={edge.fact.uuid}
                className="edge"
                data-dim={!on}
                data-focus={focus?.kind === "fact" && focus.id === edge.fact.uuid}
                data-ended={edge.fact.invalid_at !== null}
                style={{ "--c": `var(--line-${lines.indexOf(edge.line) % 10})` } as React.CSSProperties}
                tabIndex={0}
                role="button"
                aria-label={label}
                aria-pressed={pinned?.kind === "fact" && pinned.id === edge.fact.uuid}
                onMouseEnter={() => onHover(target)}
                onMouseLeave={() => onHover(null)}
                onFocus={() => onHover(target)}
                onBlur={() => onHover(null)}
                onClick={() => onPin(target)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    onPin(target);
                  }
                }}
              >
                <path className="edge-hit" d={g.path} />
                <path className="edge-line" d={g.path} />
                <path className="edge-arrow" d="M-6 -6 L5 0 L-6 6" transform={`translate(${g.midX} ${g.midY}) rotate(${g.angle})`} />
                <text className="edge-label" x={g.midX + g.normalX * 16} y={g.midY + g.normalY * 16} textAnchor="middle">
                  <tspan x={g.midX + g.normalX * 16} dy="-0.3em">
                    {edge.fact.name}
                  </tspan>
                  <tspan className="edge-date" x={g.midX + g.normalX * 16} dy="1.25em">
                    {validity(edge.fact.valid_at, edge.fact.invalid_at)}
                  </tspan>
                </text>
              </g>
            );
          })}
          {graph.nodes.map((node) => {
            const on = !focus || graph.edges.some((edge) => lit(edge) && (edge.from === node || edge.to === node));
            // The name goes on the side of the entity that has no fact line: opposite to the mean direction of its facts.
            let awayX = 0;
            let awayY = 0;
            for (const edge of graph.edges) {
              const other = edge.from === node ? edge.to : edge.to === node ? edge.from : null;
              if (other && other !== node) {
                const distance = Math.max(1, Math.hypot(other.x - node.x, other.y - node.y));
                awayX -= (other.x - node.x) / distance;
                awayY -= (other.y - node.y) / distance;
              }
            }

            const radius = node.degree > 2 ? 11 : 8;
            const sideways = Math.abs(awayX) > Math.abs(awayY) * 1.2;
            const room = awayX < 0 ? node.x : width - node.x;
            const name = clip(node.name, sideways ? Math.max(8, Math.min(nameLimit, Math.floor((room - radius - 14) / 7.4))) : nameLimit);
            let anchor: "start" | "middle" | "end" = "middle";
            let x = node.x;
            // The label of a fact is above its line, so the name of an entity goes below, unless its facts are below it.
            let y = awayY < -0.5 ? node.y - radius - 9 : node.y + radius + 18;
            if (sideways) {
              anchor = awayX < 0 ? "end" : "start";
              x = node.x + (awayX < 0 ? -1 : 1) * (radius + 8);
              y = node.y + 4.5;
            } else {
              // A name above or below must stay inside the drawing area.
              const half = name.length * 3.7;
              if (node.x - half < 6) {
                anchor = "start";
                x = 6;
              } else if (node.x + half > width - 6) {
                anchor = "end";
                x = width - 6;
              }
            }

            return (
              <g key={node.uuid} className="node" data-dim={!on}>
                <title>{node.labels.length > 0 ? `${node.name} (${node.labels.join(", ")})` : node.name}</title>
                <circle cx={node.x} cy={node.y} r={radius} />
                <text x={x} y={y} textAnchor={anchor}>
                  {name}
                </text>
              </g>
            );
          })}
        </svg>
      )}
    </div>
  );
}
