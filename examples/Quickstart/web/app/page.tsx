"use client";

import { CopilotChat, useConfigureSuggestions } from "@copilotkit/react-core/v2";

const questions = [
  "What can the eSafety Commissioner do about cyberbullying material that targets a child?",
  "What is a social media service under the Act?",
  "Which section lets the Commissioner give a removal notice for an intimate image, and who must obey it?",
];

export default function Page() {
  useConfigureSuggestions({
    suggestions: questions.map((message) => ({ title: message, message })),
    available: "always",
  });

  return (
    <main>
      <header>
        <h1>Ask the Online Safety Act 2021</h1>
        <p>
          The agent answers from a knowledge graph of the Act and names the section for each statement. Text of the
          Act: Federal Register of Legislation, CC BY 4.0.
        </p>
      </header>
      <section className="chat">
        <CopilotChat />
      </section>
    </main>
  );
}
