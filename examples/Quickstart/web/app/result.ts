// The shape of the result of the tool search_legislation, and the pure functions that turn it into a graph.
// The field names follow SearchTool.cs of the .NET server.


import { z } from "zod";

const sourceSchema = z.object({ section_id: z.string(), chain: z.string() });
const entitySchema = z.object({ uuid: z.string(), name: z.string(), labels: z.array(z.string()).default([]) });
const dateSchema = z.string().regex(/^\d{4}-\d{2}-\d{2}$/).nullable().default(null);
const factSchema = z.object({
  uuid: z.string(),
  name: z.string(),
  source: entitySchema,
  target: entitySchema,
  fact: z.string(),
  valid_at: dateSchema,
  invalid_at: dateSchema,
  sources: z.array(sourceSchema).default([]),
});
const sectionSchema = z.object({ section_id: z.string(), chain: z.string(), text: z.string() });
const resultSchema = z.object({
  query: z.string().default(""),
  count: z.number().default(0),
  facts: z.array(factSchema),
  sections: z.array(sectionSchema),
});

export type Source = z.infer<typeof sourceSchema>;
export type Entity = z.infer<typeof entitySchema>;
export type Fact = z.infer<typeof factSchema>;
export type Section = z.infer<typeof sectionSchema>;
export type SearchResult = z.infer<typeof resultSchema>;

/**
 * Reads the result text of a tool call. The AG-UI server of Agent Framework sends the JSON text of the tool as a JSON
 * string, so the text can need two parse steps. Returns null if the text does not have each field of a result of the
 * tool. The page then shows the text as it is.
 */
export function parseResult(text: string | undefined): SearchResult | null {
  if (!text) {
    return null;
  }

  try {
    let value: unknown = JSON.parse(text);
    if (typeof value === "string") {
      value = JSON.parse(value);
    }

    const parsed = resultSchema.safeParse(value);
    return parsed.success ? parsed.data : null;
  } catch {
    return null;
  }
}

/**
 * "Online Safety Act 2021 s 13" becomes "s 13". The id of a provision that the Bill changes keeps its note without the
 * name of the Bill: "s 26 (inserted by Sch 2 item 8)". Another form stays as it is.
 */
export function shortSection(sectionId: string): string {
  const at = sectionId.indexOf(" s ");
  const short = at >= 0 ? sectionId.slice(at + 1) : sectionId;
  return short.replace("the Digital Duty of Care Bill 2026, ", "").replace("Digital Duty of Care Bill 2026, ", "");
}

/** One section among the sources of a fact, with the notes of the Bill for it: "s 52" and "repealed by Sch 2 item 13". */
export type SourcePart = { section: string; notes: string[] };

/**
 * The sources of a fact as sections. A fact that the Act states and the Bill repeals has two sources with the same
 * section. They become one part: the section one time, and the change of the Bill as its note.
 */
export function sourceParts(sectionIds: string[]): SourcePart[] {
  const parts: SourcePart[] = [];
  for (const id of sectionIds) {
    const short = shortSection(id);
    const match = /^(s \d+[A-Z]*) \((.+)\)$/.exec(short);
    const section = match ? match[1] : short;
    let part = parts.find((p) => p.section === section);
    if (!part) {
      part = { section, notes: [] };
      parts.push(part);
    }

    if (match && !part.notes.includes(match[2])) {
      part.notes.push(match[2]);
    }
  }

  return parts;
}

/** The sources of a fact in one line of text: "s 52, repealed by Sch 2 item 13; s 44". */
export function sourceText(sectionIds: string[]): string {
  return sourceParts(sectionIds).map((part) => [part.section, ...part.notes].join(", ")).join("; ") || "unknown";
}

/** The first source of a fact is the line of the fact: the section that states it. */
export function lineOf(fact: Fact): string {
  return fact.sources[0]?.section_id ?? "unknown";
}

/** True if the section is one of the sources of the fact. A fact can have more than one source. */
export function statedIn(fact: Fact, sectionId: string): boolean {
  return fact.sources.length === 0 ? sectionId === "unknown" : fact.sources.some((source) => source.section_id === sectionId);
}

/**
 * The sections that state the facts. The first source of each fact comes first, because it gives the fact its colour.
 * Then come the other sources. Each section gets a line number.
 */
export function linesOf(result: SearchResult): string[] {
  const lines: string[] = [];
  const add = (line: string) => {
    if (!lines.includes(line)) {
      lines.push(line);
    }
  };
  result.facts.forEach((fact) => add(lineOf(fact)));
  result.facts.forEach((fact) => fact.sources.forEach((source) => add(source.section_id)));
  return lines;
}

export type GraphNode = { uuid: string; name: string; labels: string[]; degree: number; x: number; y: number };
export type GraphEdge = { fact: Fact; from: GraphNode; to: GraphNode; line: string; bend: number };
export type Graph = { nodes: GraphNode[]; edges: GraphEdge[]; width: number; height: number };

/**
 * Places the entities of a result on a plane. The function is deterministic: the same result gives the same picture.
 * It starts with the entities on a circle, in the order of their number of facts, and then runs a small spring model
 * (each pair of entities pushes apart, each fact pulls its two entities together).
 */
export function layoutGraph(result: SearchResult, width: number, height: number): Graph {
  const byUuid = new Map<string, GraphNode>();
  for (const fact of result.facts) {
    for (const entity of [fact.source, fact.target]) {
      const node = byUuid.get(entity.uuid);
      if (node) {
        node.degree += 1;
      } else {
        byUuid.set(entity.uuid, { ...entity, degree: 1, x: 0, y: 0 });
      }
    }
  }

  const nodes = [...byUuid.values()].sort((a, b) => b.degree - a.degree || a.name.localeCompare(b.name));
  const count = nodes.length;
  nodes.forEach((node, i) => {
    // The entity with the most facts starts in the center. The others start on a circle around it.
    const angle = (2 * Math.PI * i) / Math.max(1, count - 1);
    const radius = i === 0 ? 0 : 1;
    node.x = Math.cos(angle) * radius;
    node.y = Math.sin(angle) * radius;
  });

  const links = result.facts.map((fact) => [byUuid.get(fact.source.uuid)!, byUuid.get(fact.target.uuid)!] as const);
  const rest = 0.9;
  const gravity = 0.8;
  for (let step = 0; step < 400; step++) {
    const heat = 0.12 * (1 - step / 400);
    const move = nodes.map(() => ({ x: 0, y: 0 }));
    for (let i = 0; i < count; i++) {
      for (let j = i + 1; j < count; j++) {
        let dx = nodes[i].x - nodes[j].x;
        let dy = nodes[i].y - nodes[j].y;
        let distance = Math.hypot(dx, dy);
        if (distance < 0.01) {
          dx = 0.01 * (i + 1);
          dy = 0.01 * (j + 1);
          distance = Math.hypot(dx, dy);
        }

        const push = (rest * rest) / distance;
        move[i].x += (dx / distance) * push;
        move[i].y += (dy / distance) * push;
        move[j].x -= (dx / distance) * push;
        move[j].y -= (dy / distance) * push;
      }
    }

    for (const [a, b] of links) {
      if (a === b) {
        continue;
      }

      const dx = a.x - b.x;
      const dy = a.y - b.y;
      const distance = Math.max(0.01, Math.hypot(dx, dy));
      const pull = (distance * distance) / rest;
      const i = nodes.indexOf(a);
      const j = nodes.indexOf(b);
      move[i].x -= (dx / distance) * pull;
      move[i].y -= (dy / distance) * pull;
      move[j].x += (dx / distance) * pull;
      move[j].y += (dy / distance) * pull;
    }

    // Each entity is also pulled to the center. Without this pull, the groups of a result that are not connected move
    // far apart, and each group shrinks to a point when the model is fit into the drawing area.
    nodes.forEach((node, i) => {
      move[i].x -= node.x * gravity;
      move[i].y -= node.y * gravity;
    });

    nodes.forEach((node, i) => {
      const length = Math.max(0.01, Math.hypot(move[i].x, move[i].y));
      node.x += (move[i].x / length) * Math.min(length, heat);
      node.y += (move[i].y / length) * Math.min(length, heat);
    });
  }

  // Fit the model into the drawing area. The margin leaves room for the name of each entity.
  const marginX = Math.min(130, width * 0.16);
  const marginY = 46;
  const xs = nodes.map((n) => n.x);
  const ys = nodes.map((n) => n.y);
  const [minX, maxX, minY, maxY] = [Math.min(...xs), Math.max(...xs), Math.min(...ys), Math.max(...ys)];
  for (const node of nodes) {
    node.x = maxX > minX ? marginX + ((node.x - minX) / (maxX - minX)) * (width - 2 * marginX) : width / 2;
    node.y = maxY > minY ? marginY + ((node.y - minY) / (maxY - minY)) * (height - 2 * marginY) : height / 2;
  }

  // Two facts between the same two entities get different bends, so that no fact hides another fact.
  const seen = new Map<string, number>();
  const edges = result.facts.map((fact) => {
    const from = byUuid.get(fact.source.uuid)!;
    const to = byUuid.get(fact.target.uuid)!;
    const key = [from.uuid, to.uuid].sort().join("|");
    const index = seen.get(key) ?? 0;
    seen.set(key, index + 1);
    const side = from.uuid < to.uuid ? 1 : -1;
    // For a fact from an entity to the same entity, the bend is the size of its loop.
    const bend = from === to ? index * 34 : index === 0 ? 0 : side * Math.ceil(index / 2) * (index % 2 === 1 ? 1 : -1) * 34;
    return { fact, from, to, line: lineOf(fact), bend };
  });

  return { nodes, edges, width, height };
}

/** The place of a date on a time axis from start to end, as a number from 0 to 1. */
export function timeFraction(date: string | null, start: string, end: string): number {
  if (!date) {
    return 1;
  }

  const [a, b, t] = [Date.parse(start), Date.parse(end), Date.parse(date)];
  return b > a ? Math.min(1, Math.max(0, (t - a) / (b - a))) : 0;
}
