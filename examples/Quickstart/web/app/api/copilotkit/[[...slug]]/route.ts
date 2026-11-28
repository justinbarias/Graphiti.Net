// The CopilotKit runtime of the page. It forwards each chat request to the AG-UI server of the .NET example.
// The page holds no key. Only the .NET server holds the OpenAI key.
import { CopilotRuntime, createCopilotRuntimeHandler } from "@copilotkit/runtime/v2";
import { HttpAgent } from "@ag-ui/client";

const runtime = new CopilotRuntime({
  agents: {
    default: new HttpAgent({ url: process.env.MS_AGENT_URL ?? "http://localhost:8000/" }),
  },
});

const handler = createCopilotRuntimeHandler({
  runtime,
  basePath: "/api/copilotkit",
});

export const GET = handler;
export const POST = handler;
export const PATCH = handler;
export const DELETE = handler;
