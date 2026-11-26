// Ported from tests/llm_client/test_client.py (port/RULES.md, R25), and the tests of the retry (R23, J3) that the
// recording cannot hold: the count of attempts, the retried exception types, and each wait, with a fake TimeProvider.
// The recording of the same calls is in LlmClientClientValuesTests.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.LlmClientClientSupport;

public class TestClient
{
    private static ScriptedClient MockLLMClient() => new("MockLLMClient", new LLMConfig());

    [Fact]
    public void test_clean_input()
    {
        List<(string Input, string Expected)> testCases =
        [
            // Basic text should remain unchanged
            ("Hello World", "Hello World"),

            // Control characters should be removed
            ("Hello\u0000World", "HelloWorld"),

            // Newlines, tabs, returns should be preserved
            ("Hello\nWorld\tTest\r", "Hello\nWorld\tTest\r"),

            // Invalid Unicode should be removed
            ("Hello\udcdeWorld", "HelloWorld"),

            // Zero-width characters should be removed
            ("Hello\u200bWorld", "HelloWorld"),
            ("Test\ufeffWord", "TestWord"),

            // Multiple issues combined
            ("Hello\u0000\u200b\nWorld\udcde", "Hello\nWorld"),

            // Empty string should remain empty
            (string.Empty, string.Empty),

            // Form feed and other control characters from the error case
            ("{\"edges\":[{\"relation_typ...\f\u0004Hn\\?\"}]}", "{\"edges\":[{\"relation_typ...Hn\\?\"}]}"),

            // More specific control character tests
            ("Hello\u000cWorld", "HelloWorld"), // form feed \f
            ("Hello\u0004World", "HelloWorld"), // end of transmission

            // Combined JSON-like string with control characters
            ("{\"test\": \"value\f\u0000\u0004\"}", "{\"test\": \"value\"}"),
        ];

        // PORT: the method does not read `self`, so it is static (CA1822). The client of the upstream test stays.
        _ = MockLLMClient();
        foreach (var (input, expected) in testCases)
        {
            Assert.True(LLMClient.CleanInput(input) == expected, $"Failed for input: {Py.Repr(input)}");
        }
    }

    [Fact]
    public void test_attribute_extraction_preamble_no_op_when_disabled()
    {
        List<Message> messages = [new() { Role = "system", Content = "base" }, new() { Role = "user", Content = "hi" }];
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: false);
        Assert.Equal("base", messages[0].Content);
        Assert.Equal("hi", messages[1].Content);
    }

    [Fact]
    public void test_attribute_extraction_preamble_appends_to_system()
    {
        List<Message> messages =
        [
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "hi" },
        ];
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: true);
        Assert.StartsWith("You are helpful.", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("ATTRIBUTE EXTRACTION:", messages[0].Content, StringComparison.Ordinal);
        Assert.Contains("NEVER themselves valid values", messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("hi", messages[1].Content); // user message untouched
    }

    [Fact]
    public void test_attribute_extraction_preamble_is_idempotent()
    {
        List<Message> messages =
        [
            new() { Role = "system", Content = "You are helpful." },
            new() { Role = "user", Content = "hi" },
        ];
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: true);
        var once = messages[0].Content;
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: true);
        Assert.True(messages[0].Content == once, "second call must not double-append");
    }

    [Fact]
    public void test_attribute_extraction_preamble_falls_back_to_first_message_if_no_system()
    {
        List<Message> messages = [new() { Role = "user", Content = "hi" }];
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: true);
        Assert.Contains("ATTRIBUTE EXTRACTION:", messages[0].Content, StringComparison.Ordinal);
        Assert.EndsWith("hi", messages[0].Content, StringComparison.Ordinal);

        // Sentinel must be at the front so the idempotency check finds it.
        Assert.StartsWith("<<graphiti.attr_extraction.preamble.v1>>", messages[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void test_attribute_extraction_preamble_handles_empty_messages()
    {
        List<Message> messages = [];
        LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction: true);
        Assert.Empty(messages);
    }

    // R23, J3: 4 attempts in total, the retried exception types, and each wait. The fake clock never waits and keeps each
    // delay. Python draws the wait from random.uniform(5, high) with high = max(5, min(10 * 2 ** (attempt - 1), 120)),
    // so the waits of the three sleeps are in [5, 10], [5, 20], and [5, 40].
    [Fact]
    public async Task a_retried_error_gives_4_attempts_and_the_last_exception_is_thrown_again()
    {
        var errors = new Exception[] { new RateLimitException("1"), new RateLimitException("2"), new RateLimitException("3"), new RateLimitException("4") };
        var client = new ScriptedClient("RetryClient") { Script = n => throw errors[n] };
        var clock = new InstantTimeProvider();
        var log = new CaptureFactory();
        using var scope = PortServices.Use(time: clock, random: new PyRandom(7), loggerFactory: log);

        var thrown = await Assert.ThrowsAsync<RateLimitException>(() => client.GenerateResponseWithRetryAsync([new() { Role = "user", Content = "hi" }], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(errors[3], thrown);
        Assert.Equal(4, client.Calls.Count);
        Assert.Equal(3, clock.Delays.Count);
        double[] highs = [10, 20, 40];
        for (var i = 0; i < 3; i++)
        {
            Assert.InRange(clock.Delays[i].TotalSeconds, 5, highs[i]);
        }

        // The log runs after each failed attempt but the first: after attempts 2, 3, and 4 (tenacity's `after`).
        Assert.Equal(
            ["Retrying _generate_response_with_retry after 2 attempts...", "Retrying _generate_response_with_retry after 3 attempts...", "Retrying _generate_response_with_retry after 4 attempts..."],
            log.Lines.Select(l => l.Message).ToList());
        Assert.All(log.Lines, l => Assert.Equal(LogLevel.Warning, l.Level));
    }

    // R8: a Polly callback and the call that it makes run on the scheduler of the port.
    [Fact]
    public async Task the_attempts_run_on_the_scheduler_of_the_port()
    {
        var client = new ScriptedClient("RetryClient") { Script = n => n < 2 ? throw new EmptyResponseException("empty") : new() { ["content"] = "ok" } };
        using var scope = PortServices.Use(time: new InstantTimeProvider(), random: new PyRandom(1));

        var result = await client.GenerateResponseWithRetryAsync([new() { Role = "user", Content = "hi" }], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("ok", result["content"]);
        Assert.Equal(3, client.Calls.Count);
        Assert.All(client.Calls, c => Assert.True(c.OnLoop));
    }

    // The cache is off in the product (LLMClient is built with cache=False). With it off, no cache object exists.
    [Fact]
    public async Task with_the_cache_off_generate_response_reads_and_writes_no_cache()
    {
        var client = MockLLMClient();
        Assert.False(client.CacheEnabled);
        Assert.Null(client.CacheDir);
        var events = new List<object?>();
        client.SetTracer(new RecordingTracer(events));

        var response = await client.GenerateResponseAsync([new() { Role = "user", Content = "hi" }], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("test", response["content"]);
        var attributeEvents = events.OfType<List<object?>>().Where(e => (string)e[0]! == "add_attributes").Select(e => Dict(e[1])).ToList();
        Assert.Equal(false, attributeEvents[0]["cache.enabled"]);
        Assert.Equal(false, attributeEvents[1]["cache.hit"]);
    }
}
