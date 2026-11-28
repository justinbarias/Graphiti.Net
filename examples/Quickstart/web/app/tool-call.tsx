"use client";

import { useEffect, useMemo } from "react";
import { parseResult } from "./result";
import { eventsOf, useStore } from "./store";

type Props = {
  toolCallId: string;
  status: "inProgress" | "executing" | "complete";
  query: string;
  result: string | undefined;
};

/**
 * One tool call in the conversation. CopilotKit draws it again for each AG-UI event of the call: the start, each part
 * of the arguments, and the result. The row reports each state to the store, and the result area shows the selected call.
 */
export function ToolCallRow({ toolCallId, status, query, result }: Props) {
  const { report, select, selectedId, events } = useStore();
  useEffect(() => {
    report({ id: toolCallId, status, query, result });
  }, [report, toolCallId, status, query, result]);

  const parsed = useMemo(() => (status === "complete" ? parseResult(result) : null), [status, result]);
  const selected = selectedId === toolCallId;
  const seen = eventsOf(events, { id: toolCallId, status, query, result });
  const state = !seen.end
    ? "The arguments arrive"
    : !seen.result
      ? "The search runs"
      : parsed
        ? `${parsed.facts.length} ${parsed.facts.length === 1 ? "fact" : "facts"}, ${parsed.sections.length} ${parsed.sections.length === 1 ? "section" : "sections"}`
        : "The page cannot read the result";

  return (
    <button
      type="button"
      className="call"
      data-status={seen.result ? "complete" : "running"}
      aria-pressed={selected}
      onClick={() => {
        select(toolCallId);
        // On a narrow screen the result area is below the conversation.
        if (window.matchMedia("(max-width: 899px)").matches) {
          const area = document.getElementById("result");
          area?.scrollIntoView({ block: "start" });
          area?.focus({ preventScroll: true });
        }
      }}
    >
      <span className="call-name">
        search_legislation
        <span className="call-state">{state}</span>
      </span>
      <span className="call-query">{query || "…"}</span>
    </button>
  );
}
