// Ported from graphiti_core/prompts/summarize_nodes.py
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

namespace Graphiti.Core.Prompts;

// PORT: each response model has its fields as properties. The unit adds the members of R7.
internal sealed record Summary
{
    // PORT: the suffix Value (R2). C# does not allow a member named like its type (error CS0542), so `summary` is SummaryValue. The wire name stays `summary`.
    internal required string SummaryValue { get; set; }
}

internal sealed record SummaryDescription
{
    internal required string Description { get; set; }
}

internal interface SummarizeNodesPrompt
{
    PromptFunction SummarizePair { get; }

    PromptFunction SummarizeContext { get; }

    PromptFunction SummaryDescription { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2). Inside this class
// the name SummaryDescription is the method. The model is Graphiti.Core.Prompts.SummaryDescription.
internal static class SummarizeNodes
{
    // TODO(port): summarize_pair
    internal static List<Message> SummarizePair(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): summarize_pair");
    }

    // TODO(port): summarize_context
    internal static List<Message> SummarizeContext(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): summarize_context");
    }

    // TODO(port): summary_description
    internal static List<Message> SummaryDescription(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): summary_description");
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["summarize_pair"] = SummarizePair,
        ["summarize_context"] = SummarizeContext,
        ["summary_description"] = SummaryDescription,
    };
}

internal abstract class SummarizeNodesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "summarize_pair" => SummarizePair,
            "summarize_context" => SummarizeContext,
            "summary_description" => SummaryDescription,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt SummarizePair(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt SummarizeContext(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt SummaryDescription(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultSummarizeNodesPrompts : SummarizeNodesPrompts
{
    // TODO(port): DefaultSummarizeNodesPrompts.summarize_pair
    internal override ChatPrompt SummarizePair(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultSummarizeNodesPrompts.summarize_pair");
    }

    // TODO(port): DefaultSummarizeNodesPrompts.summarize_context
    internal override ChatPrompt SummarizeContext(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultSummarizeNodesPrompts.summarize_context");
    }

    // TODO(port): DefaultSummarizeNodesPrompts.summary_description
    internal override ChatPrompt SummaryDescription(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultSummarizeNodesPrompts.summary_description");
    }
}
