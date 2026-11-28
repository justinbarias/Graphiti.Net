"use client";

import { CopilotKit } from "@copilotkit/react-core/v2";

export function Providers({ children }: { children: React.ReactNode }) {
  // The inspector is off, so the page loads nothing from CopilotKit servers.
  return (
    <CopilotKit runtimeUrl="/api/copilotkit" agentId="default" useSingleEndpoint={false} enableInspector={false}>
      {children}
    </CopilotKit>
  );
}
