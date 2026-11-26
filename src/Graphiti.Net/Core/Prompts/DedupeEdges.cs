// Ported from graphiti_core/prompts/dedupe_edges.py
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

// PORT: a response model has its fields as properties. The unit adds the members of R7.
internal sealed record EdgeDuplicate
{
    internal required List<long> DuplicateFacts { get; set; }

    internal required List<long> ContradictedFacts { get; set; }
}

internal interface DedupeEdgesPrompt
{
    PromptFunction ResolveEdge { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class DedupeEdges
{
    // TODO(port): resolve_edge
    internal static List<Message> ResolveEdge(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): resolve_edge");
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["resolve_edge"] = ResolveEdge,
    };
}

internal abstract class DedupeEdgesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "resolve_edge" => ResolveEdge,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt ResolveEdge(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultDedupeEdgesPrompts : DedupeEdgesPrompts
{
    // TODO(port): DefaultDedupeEdgesPrompts.resolve_edge
    internal override ChatPrompt ResolveEdge(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultDedupeEdgesPrompts.resolve_edge");
    }
}
