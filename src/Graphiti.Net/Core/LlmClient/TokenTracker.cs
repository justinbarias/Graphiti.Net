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

using Graphiti.PyCompat;

/// <summary>Token usage for a single LLM call.</summary>
internal sealed record TokenUsage
{
    internal long InputTokens { get; set; }

    internal long OutputTokens { get; set; }

    internal long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>Accumulated token usage for a specific prompt type.</summary>
internal sealed record PromptTokenUsage
{
    internal required string PromptName { get; set; }

    internal long CallCount { get; set; }

    internal long TotalInputTokens { get; set; }

    internal long TotalOutputTokens { get; set; }

    internal long TotalTokens => TotalInputTokens + TotalOutputTokens;

    // PORT: Python returns the integer 0 in one branch and a float in the other (GAPS F18, R18).
    internal double AvgInputTokens => CallCount > 0 ? Py.TrueDiv(TotalInputTokens, CallCount) : 0;

    internal double AvgOutputTokens => CallCount > 0 ? Py.TrueDiv(TotalOutputTokens, CallCount) : 0;
}

/// <summary>Thread-safe tracker for LLM token usage by prompt type.</summary>
internal sealed class TokenUsageTracker
{
    private readonly OrderedDictionary<string, PromptTokenUsage> usage;

    private readonly Lock lockValue;

    internal TokenUsageTracker()
    {
        usage = [];
        lockValue = new Lock();
    }

    /// <summary>
    /// Record token usage for a prompt.
    /// </summary>
    /// <param name="promptName">Name of the prompt (e.g., 'extract_nodes.extract_message').</param>
    /// <param name="inputTokens">Number of input tokens used.</param>
    /// <param name="outputTokens">Number of output tokens generated.</param>
    internal void Record(string? promptName, long inputTokens, long outputTokens)
    {
        var key = !string.IsNullOrEmpty(promptName) ? promptName : "unknown";

        lock (lockValue)
        {
            if (!usage.TryGetValue(key, out var entry))
            {
                entry = new PromptTokenUsage { PromptName = key };
                usage[key] = entry;
            }

            entry.CallCount += 1;
            entry.TotalInputTokens += inputTokens;
            entry.TotalOutputTokens += outputTokens;
        }
    }

    /// <summary>Get a copy of current token usage by prompt type.</summary>
    internal OrderedDictionary<string, PromptTokenUsage> GetUsage()
    {
        lock (lockValue)
        {
            var copy = new OrderedDictionary<string, PromptTokenUsage>();
            foreach (var (k, v) in usage)
            {
                copy[k] = new PromptTokenUsage
                {
                    PromptName = v.PromptName,
                    CallCount = v.CallCount,
                    TotalInputTokens = v.TotalInputTokens,
                    TotalOutputTokens = v.TotalOutputTokens,
                };
            }

            return copy;
        }
    }

    /// <summary>Get total token usage across all prompts.</summary>
    internal TokenUsage GetTotalUsage()
    {
        lock (lockValue)
        {
            var totalInput = usage.Values.Sum(u => u.TotalInputTokens);
            var totalOutput = usage.Values.Sum(u => u.TotalOutputTokens);
            return new TokenUsage { InputTokens = totalInput, OutputTokens = totalOutput };
        }
    }

    /// <summary>Reset all tracked usage.</summary>
    internal void Reset()
    {
        lock (lockValue)
        {
            usage.Clear();
        }
    }

    /// <summary>
    /// Print a formatted summary of token usage.
    /// </summary>
    /// <param name="sortBy">Sort key - 'total_tokens', 'input_tokens', 'output_tokens', 'call_count', or 'prompt_name'.</param>
    internal void PrintSummary(string sortBy = "total_tokens")
    {
        var usage = GetUsage();
        if (usage.Count == 0)
        {
            Console.Out.WriteLine("No token usage recorded.");
            return;
        }

        // Sort usage
        // PORT: sort_keys.get(sort_by, sort_keys['total_tokens']) is the last arm. The keys with numbers sort in
        // descending order, and prompt_name in ascending order. Both orders are stable (R14).
        var sortedUsage = sortBy switch
        {
            "input_tokens" => usage.OrderByDescending(x => x.Value.TotalInputTokens).ToList(),
            "output_tokens" => usage.OrderByDescending(x => x.Value.TotalOutputTokens).ToList(),
            "call_count" => usage.OrderByDescending(x => x.Value.CallCount).ToList(),
            "prompt_name" => usage.OrderBy(x => x.Key, Py.StringComparer).ToList(),
            _ => usage.OrderByDescending(x => x.Value.TotalTokens).ToList(),
        };

        // Print header
        Console.Out.WriteLine("\n" + new string('=', 100));
        Console.Out.WriteLine("TOKEN USAGE SUMMARY");
        Console.Out.WriteLine(new string('=', 100));
        Console.Out.WriteLine(
            Py.Format("Prompt Type", "<45") + " " + Py.Format("Calls", ">8") + " " + Py.Format("Input", ">12") + " "
            + Py.Format("Output", ">12") + " " + Py.Format("Total", ">12") + " " + Py.Format("Avg In", ">10") + " "
            + Py.Format("Avg Out", ">10"));
        Console.Out.WriteLine(new string('-', 100));

        // Print each prompt's usage
        foreach (var (promptName, promptUsage) in sortedUsage)
        {
            Console.Out.WriteLine(
                Py.Format(promptName, "<45") + " " + Py.Format(promptUsage.CallCount, ">8") + " "
                + Py.Format(promptUsage.TotalInputTokens, ">12,") + " "
                + Py.Format(promptUsage.TotalOutputTokens, ">12,") + " " + Py.Format(promptUsage.TotalTokens, ">12,") + " "
                + Py.Format(promptUsage.AvgInputTokens, ">10,.1f") + " " + Py.Format(promptUsage.AvgOutputTokens, ">10,.1f"));
        }

        // Print totals
        var total = GetTotalUsage();
        var totalCalls = usage.Values.Sum(u => u.CallCount);
        Console.Out.WriteLine(new string('-', 100));
        Console.Out.WriteLine(
            Py.Format("TOTAL", "<45") + " " + Py.Format(totalCalls, ">8") + " " + Py.Format(total.InputTokens, ">12,") + " "
            + Py.Format(total.OutputTokens, ">12,") + " " + Py.Format(total.TotalTokens, ">12,"));
        Console.Out.WriteLine(new string('=', 100) + "\n");
    }
}
