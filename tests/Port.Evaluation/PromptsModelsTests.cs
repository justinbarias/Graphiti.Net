// Ported from tests/prompts/test_legacy_prompt_compatibility.py: the two tests whose code under test is
// graphiti_core/prompts/models.py (R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Prompts;

public class PromptsModelsTests
{
    // The parameters of @pytest.mark.parametrize('messages', [...]). A test cannot take a Message
    // parameter, because the type is internal, so each row is an index into this list.
    private static List<List<Message>> InvalidMessageShapes() =>
    [
        [],
        [new Message { Role = "system", Content = "system" }],
        [
            new Message { Role = "user", Content = "user" },
            new Message { Role = "system", Content = "system" },
        ],
        [
            new Message { Role = "assistant", Content = "assistant" },
            new Message { Role = "user", Content = "user" },
        ],
        [
            new Message { Role = "system", Content = "system" },
            new Message { Role = "user", Content = "user" },
            new Message { Role = "user", Content = "extra" },
        ],
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void test_chat_prompt_rejects_invalid_message_shapes(int index)
    {
        var messages = InvalidMessageShapes()[index];

        Assert.Throws<ArgumentException>(() => ChatPrompt.FromMessages(messages));
    }

    [Fact]
    public void test_chat_prompt_from_messages_copies_content_without_unicode_note()
    {
        List<Message> messages =
        [
            new Message { Role = "system", Content = "system prompt" },
            new Message { Role = "user", Content = "user prompt" },
        ];

        var prompt = ChatPrompt.FromMessages(messages);

        Assert.Equal("system prompt", prompt.System.Content);
        Assert.Equal("user prompt", prompt.User.Content);
    }
}
