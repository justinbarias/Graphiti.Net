// Ported from graphiti_core/llm_client/token_tracker.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.LlmClient;

internal sealed record TokenUsage
{
    internal long InputTokens { get; set; }

    internal long OutputTokens { get; set; }

    // TODO(port): TokenUsage.total_tokens
    internal long TotalTokens => throw new NotSupportedException("TODO(port): TokenUsage.total_tokens");
}

internal sealed record PromptTokenUsage
{
    internal required string PromptName { get; set; }

    internal long CallCount { get; set; }

    internal long TotalInputTokens { get; set; }

    internal long TotalOutputTokens { get; set; }

    // TODO(port): PromptTokenUsage.total_tokens
    internal long TotalTokens => throw new NotSupportedException("TODO(port): PromptTokenUsage.total_tokens");

    // PORT: Python returns the integer 0 in one branch and a float in the other (GAPS F18, R18).
    // TODO(port): PromptTokenUsage.avg_input_tokens
    internal double AvgInputTokens => throw new NotSupportedException("TODO(port): PromptTokenUsage.avg_input_tokens");

    // TODO(port): PromptTokenUsage.avg_output_tokens
    internal double AvgOutputTokens => throw new NotSupportedException("TODO(port): PromptTokenUsage.avg_output_tokens");
}

// PORT: the private attributes `_usage` and `_lock` (a System.Threading.Lock, R8) are not declared. The unit adds
// them with the constructor body.
internal sealed class TokenUsageTracker
{
    // TODO(port): TokenUsageTracker.__init__
    internal TokenUsageTracker()
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.__init__");
    }

    // TODO(port): TokenUsageTracker.record
    internal void Record(string? promptName, long inputTokens, long outputTokens)
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.record");
    }

    // TODO(port): TokenUsageTracker.get_usage
    internal OrderedDictionary<string, PromptTokenUsage> GetUsage()
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.get_usage");
    }

    // TODO(port): TokenUsageTracker.get_total_usage
    internal TokenUsage GetTotalUsage()
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.get_total_usage");
    }

    // TODO(port): TokenUsageTracker.reset
    internal void Reset()
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.reset");
    }

    // TODO(port): TokenUsageTracker.print_summary
    internal void PrintSummary(string sortBy = "total_tokens")
    {
        throw new NotSupportedException("TODO(port): TokenUsageTracker.print_summary");
    }
}
