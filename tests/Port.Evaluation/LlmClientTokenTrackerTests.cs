// Ported from tests/llm_client/test_token_tracker.py (port/RULES.md, R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.LlmClient;

public class TestTokenUsage
{
    [Fact]
    public void test_total_tokens()
    {
        // Test that total_tokens correctly sums input and output tokens.
        var usage = new TokenUsage { InputTokens = 100, OutputTokens = 50 };
        Assert.Equal(150, usage.TotalTokens);
    }

    [Fact]
    public void test_default_values()
    {
        // Test that default values are zero.
        var usage = new TokenUsage();
        Assert.Equal(0, usage.InputTokens);
        Assert.Equal(0, usage.OutputTokens);
        Assert.Equal(0, usage.TotalTokens);
    }
}

public class TestPromptTokenUsage
{
    [Fact]
    public void test_total_tokens()
    {
        // Test that total_tokens correctly sums input and output tokens.
        var usage = new PromptTokenUsage
        {
            PromptName = "test",
            CallCount = 5,
            TotalInputTokens = 1000,
            TotalOutputTokens = 500,
        };
        Assert.Equal(1500, usage.TotalTokens);
    }

    [Fact]
    public void test_avg_input_tokens()
    {
        // Test average input tokens calculation.
        var usage = new PromptTokenUsage
        {
            PromptName = "test",
            CallCount = 4,
            TotalInputTokens = 1000,
            TotalOutputTokens = 500,
        };
        Assert.Equal(250.0, usage.AvgInputTokens);
    }

    [Fact]
    public void test_avg_output_tokens()
    {
        // Test average output tokens calculation.
        var usage = new PromptTokenUsage
        {
            PromptName = "test",
            CallCount = 4,
            TotalInputTokens = 1000,
            TotalOutputTokens = 500,
        };
        Assert.Equal(125.0, usage.AvgOutputTokens);
    }

    [Fact]
    public void test_avg_tokens_zero_calls()
    {
        // Test that average returns 0 when call_count is zero.
        var usage = new PromptTokenUsage
        {
            PromptName = "test",
            CallCount = 0,
            TotalInputTokens = 0,
            TotalOutputTokens = 0,
        };
        Assert.Equal(0, usage.AvgInputTokens);
        Assert.Equal(0, usage.AvgOutputTokens);
    }
}

public class TestTokenUsageTracker
{
    [Fact]
    public void test_record_new_prompt()
    {
        // Test recording usage for a new prompt.
        var tracker = new TokenUsageTracker();
        tracker.Record("extract_nodes", 100, 50);

        var usage = tracker.GetUsage();
        Assert.Contains("extract_nodes", usage.Keys);
        Assert.Equal(1, usage["extract_nodes"].CallCount);
        Assert.Equal(100, usage["extract_nodes"].TotalInputTokens);
        Assert.Equal(50, usage["extract_nodes"].TotalOutputTokens);
    }

    [Fact]
    public void test_record_existing_prompt()
    {
        // Test that multiple calls accumulate correctly.
        var tracker = new TokenUsageTracker();
        tracker.Record("extract_nodes", 100, 50);
        tracker.Record("extract_nodes", 200, 100);

        var usage = tracker.GetUsage();
        Assert.Equal(2, usage["extract_nodes"].CallCount);
        Assert.Equal(300, usage["extract_nodes"].TotalInputTokens);
        Assert.Equal(150, usage["extract_nodes"].TotalOutputTokens);
    }

    [Fact]
    public void test_record_none_prompt_name()
    {
        // Test that None prompt_name is recorded as 'unknown'.
        var tracker = new TokenUsageTracker();
        tracker.Record(null, 100, 50);

        var usage = tracker.GetUsage();
        Assert.Contains("unknown", usage.Keys);
        Assert.Equal(1, usage["unknown"].CallCount);
    }

    [Fact]
    public void test_record_multiple_prompts()
    {
        // Test recording usage for multiple different prompts.
        var tracker = new TokenUsageTracker();
        tracker.Record("extract_nodes", 100, 50);
        tracker.Record("dedupe_nodes", 200, 100);
        tracker.Record("extract_edges", 150, 75);

        var usage = tracker.GetUsage();
        Assert.Equal(3, usage.Count);
        Assert.Contains("extract_nodes", usage.Keys);
        Assert.Contains("dedupe_nodes", usage.Keys);
        Assert.Contains("extract_edges", usage.Keys);
    }

    [Fact]
    public void test_get_usage_returns_copy()
    {
        // Test that get_usage returns a copy, not the internal dict.
        var tracker = new TokenUsageTracker();
        tracker.Record("test", 100, 50);

        var usage1 = tracker.GetUsage();
        usage1["test"].TotalInputTokens = 9999;

        var usage2 = tracker.GetUsage();
        Assert.Equal(100, usage2["test"].TotalInputTokens); // Original unchanged
    }

    [Fact]
    public void test_get_total_usage()
    {
        // Test getting total usage across all prompts.
        var tracker = new TokenUsageTracker();
        tracker.Record("extract_nodes", 100, 50);
        tracker.Record("dedupe_nodes", 200, 100);
        tracker.Record("extract_edges", 150, 75);

        var total = tracker.GetTotalUsage();
        Assert.Equal(450, total.InputTokens);
        Assert.Equal(225, total.OutputTokens);
        Assert.Equal(675, total.TotalTokens);
    }

    [Fact]
    public void test_get_total_usage_empty()
    {
        // Test getting total usage when no records exist.
        var tracker = new TokenUsageTracker();
        var total = tracker.GetTotalUsage();
        Assert.Equal(0, total.InputTokens);
        Assert.Equal(0, total.OutputTokens);
    }

    [Fact]
    public void test_reset()
    {
        // Test that reset clears all tracked usage.
        var tracker = new TokenUsageTracker();
        tracker.Record("extract_nodes", 100, 50);
        tracker.Record("dedupe_nodes", 200, 100);

        tracker.Reset();

        var usage = tracker.GetUsage();
        Assert.Empty(usage);

        var total = tracker.GetTotalUsage();
        Assert.Equal(0, total.TotalTokens);
    }

    [Fact]
    public async Task test_thread_safety()
    {
        // Test that concurrent access from multiple threads is safe.
        var tracker = new TokenUsageTracker();
        var numThreads = 10;
        var callsPerThread = 100;

        void RecordTokens(int threadId)
        {
            for (var i = 0; i < callsPerThread; i++)
            {
                tracker.Record($"prompt_{threadId}", 10, 5);
            }
        }

        // PORT: ThreadPoolExecutor(max_workers=num_threads) is one thread-pool task for each worker. A test may use
        // Task.Run: R8 forbids it in ported code only.
        var futures = Enumerable.Range(0, numThreads).Select(i => Task.Run(() => RecordTokens(i), TestContext.Current.CancellationToken)).ToList();
        await Task.WhenAll(futures);

        var usage = tracker.GetUsage();
        Assert.Equal(numThreads, usage.Count);

        var total = tracker.GetTotalUsage();
        var expectedInput = numThreads * callsPerThread * 10;
        var expectedOutput = numThreads * callsPerThread * 5;
        Assert.Equal(expectedInput, total.InputTokens);
        Assert.Equal(expectedOutput, total.OutputTokens);
    }

    [Fact]
    public async Task test_concurrent_same_prompt()
    {
        // Test concurrent access to the same prompt name.
        var tracker = new TokenUsageTracker();
        var numThreads = 10;
        var callsPerThread = 100;

        void RecordTokens()
        {
            for (var i = 0; i < callsPerThread; i++)
            {
                tracker.Record("shared_prompt", 10, 5);
            }
        }

        var futures = Enumerable.Range(0, numThreads).Select(_ => Task.Run(RecordTokens, TestContext.Current.CancellationToken)).ToList();
        await Task.WhenAll(futures);

        var usage = tracker.GetUsage();
        Assert.Equal(numThreads * callsPerThread, usage["shared_prompt"].CallCount);
        Assert.Equal(numThreads * callsPerThread * 10, usage["shared_prompt"].TotalInputTokens);
        Assert.Equal(numThreads * callsPerThread * 5, usage["shared_prompt"].TotalOutputTokens);
    }
}
