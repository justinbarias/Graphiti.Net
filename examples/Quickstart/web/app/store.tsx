"use client";

import { createContext, useCallback, useContext, useMemo, useRef, useState } from "react";

/** One call of the tool search_legislation, as the AG-UI events of the agent show it so far. */
export type ToolCall = {
  id: string;
  /** inProgress: the call started and its arguments arrive. executing: the arguments are complete. complete: the result arrived. */
  status: "inProgress" | "executing" | "complete";
  query: string;
  result: string | undefined;
};

/** The AG-UI events of a tool call that the page received so far. */
export type CallEvents = { start: boolean; args: boolean; end: boolean; result: boolean };
export type CallEvent = keyof CallEvents;

/** What the reader points at. The graph, the table, and the section texts show only the facts of this focus. */
export type Focus = { kind: "line"; id: string } | { kind: "fact"; id: string } | null;

type Store = {
  calls: ToolCall[];
  selectedId: string | null;
  events: Record<string, CallEvents>;
  report: (call: ToolCall) => void;
  mark: (id: string, event: CallEvent) => void;
  select: (id: string) => void;
  hover: Focus;
  pinned: Focus;
  setHover: (focus: Focus) => void;
  togglePin: (focus: Focus) => void;
};

const StoreContext = createContext<Store | null>(null);

export function StoreProvider({ children }: { children: React.ReactNode }) {
  const [calls, setCalls] = useState<ToolCall[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [hover, setHover] = useState<Focus>(null);
  const [pinned, setPinned] = useState<Focus>(null);
  const [events, setEvents] = useState<Record<string, CallEvents>>({});

  const mark = useCallback((id: string, event: CallEvent) => {
    setEvents((before) => {
      const old = before[id] ?? { start: false, args: false, end: false, result: false };
      return old[event] ? before : { ...before, [id]: { ...old, [event]: true } };
    });
  }, []);

  const known = useRef(new Set<string>());
  const report = useCallback((call: ToolCall) => {
    if (!known.current.has(call.id)) {
      // A new call takes the result area, so that the reader sees the events as they arrive.
      known.current.add(call.id);
      setSelectedId(call.id);
      setPinned(null);
      setHover(null);
    }

    setCalls((before) => {
      const at = before.findIndex((c) => c.id === call.id);
      if (at < 0) {
        return [...before, call];
      }

      const old = before[at];
      if (old.status === call.status && old.query === call.query && old.result === call.result) {
        return before;
      }

      return before.map((c, i) => (i === at ? call : c));
    });
  }, []);

  const select = useCallback((id: string) => {
    setSelectedId(id);
    setPinned(null);
    setHover(null);
  }, []);

  const togglePin = useCallback((focus: Focus) => {
    setPinned((before) => (before && focus && before.kind === focus.kind && before.id === focus.id ? null : focus));
  }, []);

  const store = useMemo(
    () => ({ calls, selectedId, events, report, mark, select, hover, pinned, setHover, togglePin }),
    [calls, selectedId, events, report, mark, select, hover, pinned, togglePin],
  );
  return <StoreContext.Provider value={store}>{children}</StoreContext.Provider>;
}

export function useStore(): Store {
  const store = useContext(StoreContext);
  if (!store) {
    throw new Error("useStore needs a StoreProvider above it.");
  }

  return store;
}

/**
 * The events of one call. The page records the AG-UI events of the agent as they arrive. The status of the renderer
 * fills the gaps: a call that the renderer shows has started, and a call with a result has all four events.
 */
export function eventsOf(events: Record<string, CallEvents>, call: ToolCall): CallEvents {
  const seen = events[call.id] ?? { start: false, args: false, end: false, result: false };
  const result = seen.result || call.status === "complete";
  const end = result || seen.end || call.status === "executing";
  return { start: true, args: end || seen.args || call.query !== "", end, result };
}
