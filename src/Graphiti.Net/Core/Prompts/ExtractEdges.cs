// Ported from graphiti_core/prompts/extract_edges.py
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

// PORT: each response model has its fields as properties. The unit adds the members of R7. The model `Edge` has the
// name of the shell `Graphiti.Core.Edge` of edges.py. A file in a namespace below Graphiti.Core with
// `using Graphiti.Core.Prompts;` binds `Edge` to this model.
internal sealed record Edge
{
    internal required string SourceEntityName { get; set; }

    internal required string TargetEntityName { get; set; }

    internal required string RelationType { get; set; }

    internal required string Fact { get; set; }

    internal string? ValidAt { get; set; }

    internal string? InvalidAt { get; set; }

    internal List<long> EpisodeIndices { get; set; } = [0];
}

internal sealed record ExtractedEdges
{
    internal required List<Edge> Edges { get; set; }
}

internal sealed record EdgeTimestamps
{
    internal string? ValidAt { get; set; }

    internal string? InvalidAt { get; set; }
}

internal sealed record BatchEdgeTimestamps
{
    internal required List<EdgeTimestamps> Timestamps { get; set; }
}

internal interface ExtractEdgesPrompt
{
    PromptFunction Edge { get; }

    PromptFunction ExtractAttributes { get; }

    PromptFunction ExtractTimestamps { get; }

    PromptFunction ExtractTimestampsBatch { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class ExtractEdges
{
    // TODO(port): edge
    internal static List<Message> Edge(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): edge");
    }

    // TODO(port): extract_attributes
    internal static List<Message> ExtractAttributes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_attributes");
    }

    // TODO(port): extract_timestamps
    internal static List<Message> ExtractTimestamps(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_timestamps");
    }

    // TODO(port): extract_timestamps_batch
    internal static List<Message> ExtractTimestampsBatch(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_timestamps_batch");
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["edge"] = Edge,
        ["extract_attributes"] = ExtractAttributes,
        ["extract_timestamps"] = ExtractTimestamps,
        ["extract_timestamps_batch"] = ExtractTimestampsBatch,
    };
}

internal abstract class ExtractEdgesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "edge" => Edge,
            "extract_attributes" => ExtractAttributes,
            "extract_timestamps" => ExtractTimestamps,
            "extract_timestamps_batch" => ExtractTimestampsBatch,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt Edge(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractAttributes(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractTimestamps(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractTimestampsBatch(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultExtractEdgesPrompts : ExtractEdgesPrompts
{
    // TODO(port): DefaultExtractEdgesPrompts.edge
    internal override ChatPrompt Edge(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractEdgesPrompts.edge");
    }

    // TODO(port): DefaultExtractEdgesPrompts.extract_attributes
    internal override ChatPrompt ExtractAttributes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractEdgesPrompts.extract_attributes");
    }

    // TODO(port): DefaultExtractEdgesPrompts.extract_timestamps
    internal override ChatPrompt ExtractTimestamps(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractEdgesPrompts.extract_timestamps");
    }

    // TODO(port): DefaultExtractEdgesPrompts.extract_timestamps_batch
    internal override ChatPrompt ExtractTimestampsBatch(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractEdgesPrompts.extract_timestamps_batch");
    }
}
