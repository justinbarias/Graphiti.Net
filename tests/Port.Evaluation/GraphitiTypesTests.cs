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

    [Fact]
    public void uses_prompt_routing_is_false_without_clients()
    {
        Assert.False(GraphitiTypes.UsesPromptRouting(null));
    }
}
