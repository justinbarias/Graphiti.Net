// Evaluation test for graphiti_core/graphiti_types.py (port/RULES.md, R25).
// The upstream test is tests/prompts/test_complete_prompt.py. Only the tests whose path stops at no skeleton member
// are here. The others wait: see the report of the unit.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;

public class GraphitiTypesTests
{
    [Fact]
    public void test_resolve_prompt_messages_uses_legacy_prompt_without_clients()
    {
        List<Message> legacyMessages = [new Message { Role = "user", Content = "legacy prompt" }];

        var messages = GraphitiTypes.ResolvePromptMessages(
            "summarize_sagas.summarize_saga",
            _ => legacyMessages,
            new OrderedDictionary<string, object?> { ["saga_name"] = "test saga" });

        Assert.Equal(legacyMessages, messages);
    }

    // Python has no test for this branch of resolve_prompt_messages: clients that hold neither a runtime nor a prompt
    // library take the legacy path, as no clients do. No LLM client, driver, embedder, or reranker can be built until the
    // layers of those classes are ported (their constructors are skeleton members), and the code under test reads only
    // llm_runtime and prompt_library, so the other fields stay null here and nothing reads them.
    [Fact]
    public void resolve_prompt_messages_uses_legacy_prompt_when_clients_have_no_runtime_and_no_prompt_library()
    {
        var clients = new GraphitiClients
        {
            Driver = null!,
            LlmClient = null!,
            Embedder = null!,
            CrossEncoder = null!,
            Tracer = new NoOpTracer(),
        };
        List<Message> legacyMessages = [new Message { Role = "user", Content = "legacy prompt" }];

        Assert.False(GraphitiTypes.UsesPromptRouting(clients));
        Assert.False(GraphitiTypes.UsesPromptRouting(null));
        Assert.Same(
            legacyMessages,
            GraphitiTypes.ResolvePromptMessages(
                "summarize_sagas.summarize_saga",
                _ => legacyMessages,
                new OrderedDictionary<string, object?> { ["saga_name"] = "test saga" },
                clients));
    }
}
