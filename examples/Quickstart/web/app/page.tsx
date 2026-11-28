"use client";

import { useEffect } from "react";
import { CopilotChat, useAgent, useConfigureSuggestions, useRenderTool } from "@copilotkit/react-core/v2";
import { z } from "zod";
import { Inspector } from "./inspector";
import { StoreProvider, useStore } from "./store";
import { ToolCallRow } from "./tool-call";

const questions = [
  "What can the eSafety Commissioner do about cyberbullying material that targets a child?",
  "What is a social media service under the Act?",
  "Which section lets the Commissioner give a removal notice for an intimate image, and who must obey it?",
];

function Workspace() {
  useConfigureSuggestions({
    suggestions: questions.map((message) => ({ title: message, message })),
    available: "always",
  });

  // The AG-UI events of each tool call of the backend agent, as they arrive. The status of the renderer below does not
  // show the event TOOL_CALL_END of a backend tool, so the page records the events from the agent.
  const { agent } = useAgent({ agentId: "default" });
  const { mark } = useStore();
  useEffect(() => {
    const subscription = agent.subscribe({
      onToolCallStartEvent: ({ event }) => mark(event.toolCallId, "start"),
      onToolCallArgsEvent: ({ event }) => mark(event.toolCallId, "args"),
      onToolCallEndEvent: ({ event }) => mark(event.toolCallId, "end"),
      onToolCallResultEvent: ({ event }) => mark(event.toolCallId, "result"),
    });
    return () => subscription.unsubscribe();
  }, [agent, mark]);

  // The renderer of the tool of the .NET agent. CopilotKit draws it again as the call changes: with the arguments so
  // far while they arrive, and with the status complete and the result text when the result arrives.
  useRenderTool(
    {
      name: "search_legislation",
      parameters: z.object({ query: z.string() }),
      render: ({ toolCallId, status, parameters, result }) => (
        <ToolCallRow toolCallId={toolCallId} status={status} query={parameters.query ?? ""} result={result} />
      ),
    },
    [],
  );

  return (
    <div className="shell">
      <aside className="rail dark">
        <header>
          <h1>Ask the Online Safety Act 2021</h1>
          <p>An agent answers from a temporal graph of the Act. Each search of the graph shows as a row in the conversation.</p>
        </header>
        <div className="chat">
          <CopilotChat labels={{ chatInputPlaceholder: "Ask about the Act", welcomeMessageText: "Ask a question, or start with one of these." }} />
        </div>
        <footer>
          Text of the Act: Federal Register of Legislation, <a href="https://creativecommons.org/licenses/by/4.0/">CC BY 4.0</a>.
        </footer>
      </aside>
      <main id="result" className="result" tabIndex={-1}>
        <Inspector />
      </main>
    </div>
  );
}

export default function Page() {
  return (
    <StoreProvider>
      <Workspace />
    </StoreProvider>
  );
}
