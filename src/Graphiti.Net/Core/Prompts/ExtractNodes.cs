// Ported from graphiti_core/prompts/extract_nodes.py
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
internal sealed record ExtractedEntity
{
    internal required string Name { get; set; }

    internal required long EntityTypeId { get; set; }

    internal List<long> EpisodeIndices { get; set; } = [0];
}

internal sealed record ExtractedEntities
{
    // TODO(port): no rule for a field with the name of its class. C# does not allow a member named like its type
    // (error CS0542), so `extracted_entities` is ExtractedEntitiesValue. The wire name stays `extracted_entities`.
    internal required List<ExtractedEntity> ExtractedEntitiesValue { get; set; }
}

internal sealed record EntitySummary
{
    internal required string Summary { get; set; }
}

internal sealed record SummarizedEntity
{
    internal required string Name { get; set; }

    internal required string Summary { get; set; }
}

internal sealed record SummarizedEntities
{
    internal required List<SummarizedEntity> Summaries { get; set; }
}

internal interface ExtractNodesPrompt
{
    PromptFunction ExtractMessage { get; }

    PromptFunction ExtractJson { get; }

    PromptFunction ExtractText { get; }

    PromptFunction ClassifyNodes { get; }

    PromptFunction ExtractAttributes { get; }

    PromptFunction ExtractSummary { get; }

    PromptFunction ExtractSummariesBatch { get; }

    PromptFunction ExtractEntitySummariesFromEpisodes { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class ExtractNodes
{
    // TODO(port): extract_message
    internal static List<Message> ExtractMessage(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_message");
    }

    // TODO(port): extract_json
    internal static List<Message> ExtractJson(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_json");
    }

    // TODO(port): extract_text
    internal static List<Message> ExtractText(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_text");
    }

    // TODO(port): classify_nodes
    internal static List<Message> ClassifyNodes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): classify_nodes");
    }

    // TODO(port): extract_attributes
    internal static List<Message> ExtractAttributes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_attributes");
    }

    // TODO(port): extract_summary
    internal static List<Message> ExtractSummary(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_summary");
    }

    // PORT: internal, because judge cases call it (R2).
    // TODO(port): _entity_type_descriptions_section
    internal static string EntityTypeDescriptionsSection(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): _entity_type_descriptions_section");
    }

    // TODO(port): extract_summaries_batch
    internal static List<Message> ExtractSummariesBatch(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_summaries_batch");
    }

    // NOTE: This prompt is semantically mirrored in the Go async summary worker at
    // src/lib/graphsummary/processor.go (entitySummarySystemPrompt). Keep both in sync.
    // PORT: the Python literal joins lines with a backslash at the line end (R9). This text holds the joined lines.
    private const string EntityEpisodeSummarySystemPrompt = """
You maintain detailed, information-dense entity memories from episode text.

Use ONLY facts explicitly stated in EPISODES and durable facts already present in EXISTING_SUMMARY.
NEVER infer beyond what is directly supported.

Primary goal:
Write a dense factual summary of the entity that preserves as many supported details as possible while staying coherent and durable.

When the input includes entity_type_descriptions, use them to decide which facts are most relevant to the entity type. NEVER mention the entity type, type description, or classification in the summary text itself.

What to capture:
- Stable facts about the entity
- All materially relevant named people, organizations, places, events, documents, objects, and other entities linked to it
- Explicit actions, roles, responsibilities, relationships, and outcomes
- Counts, sequences, and repeated patterns when the evidence supports them
- Temporal details at the highest fidelity available: dates, months, years, ordering, and changes over time
- Current state over superseded state when newer episodes clearly update older information

Rules:
- Be exhaustive within the evidence. Prefer retaining a supported concrete detail over omitting it for brevity.
- NEVER infer preferences, habits, recurrence, frequency, causality, intent, importance, or category from a name, a single mention, or weak evidence.
- Only describe something as recurring, preferred, typical, habitual, or ongoing when multiple episodes explicitly support that claim or one episode states it directly.
- Include all materially relevant named participants that appear in the evidence.
- Include temporal qualifiers whenever they are available.
- Mention counts when they are directly supported and meaningful. Prefer direct factual phrasing over meta phrasing.
- When the durable fact is the content of what was said, state the content directly instead of describing that it was said.
- Use communication verbs only when the act of speaking, asking, sharing, presenting, announcing, or telling is itself the important fact.
- NEVER manufacture pattern language from a single occurrence. A single mention can support a fact, but not a trend, habit, or preference unless the text states that directly.
- If the evidence is insufficient or ambiguous, omit the claim.
- NEVER mention the source material or summarization process.
- NEVER mention episodes, messages, prompts, summaries, memory, graphs, nodes, labels, node types, ontology, schema, or categorization.
- NEVER output phrases like "the summary", "the entity", "categorized as", "tagged as", "suggests", "implies", "appears to", or "recorded interaction".
- NEVER use "the entity" as a pronoun. Use the entity's actual name or a natural pronoun (he, she, it, they).
- NEVER use meta-language verbs like "mentioned", "described", "stated", "noted", "discussed", "referenced", "indicated", or "reported". State the fact directly instead of describing how it was communicated.
- NEVER begin the summary with "A ", "An ", or "This is". If the entity's name starts with "The" (e.g. "The Washington Post"), that is acceptable; otherwise NEVER lead with "The ". Lead with the entity's name or a concrete fact.
- When newer episode text conflicts with older summary content, prefer the newer explicit fact.
- If the new episodes add no durable fact, return the existing summary unchanged.
- The summary should read like a compact brief, not a tagline.
- Write 2-6 dense sentences in third person.
- Return only the summary text.

<EXAMPLES>
Input: {"name": "Jordan Lee", "existing_summary": "Jordan Lee works at Belmont Arts Center.", "episodes": [{"content": "Mina: Jordan Lee presented a ceramics workshop at Belmont Arts Center on March 3, 2025. The workshop had 24 attendees and focused on wheel-thrown bowls.\nOwen: After the session, Jordan announced a second April workshop for returning students."}, {"content": "Mina: Jordan shared that the new kiln room opened last month and that Jordan now supervises two studio assistants.\nOwen: Jordan still teaches beginner ceramics on Wednesday evenings."}]}
GOOD: "Jordan Lee works at Belmont Arts Center. Jordan presented a ceramics workshop there on March 3, 2025 for 24 attendees focused on wheel-thrown bowls, and later announced a second April workshop for returning students. Jordan supervises two studio assistants, teaches beginner ceramics on Wednesday evenings, and works out of the new kiln room that opened the previous month."
BAD: "Jordan Lee seems interested in ceramics. Jordan mentioned teaching and was described as busy at the arts center."
</EXAMPLES>
""";

    // TODO(port): extract_entity_summaries_from_episodes
    internal static List<Message> ExtractEntitySummariesFromEpisodes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): extract_entity_summaries_from_episodes");
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["extract_message"] = ExtractMessage,
        ["extract_json"] = ExtractJson,
        ["extract_text"] = ExtractText,
        ["extract_summary"] = ExtractSummary,
        ["extract_summaries_batch"] = ExtractSummariesBatch,
        ["extract_entity_summaries_from_episodes"] = ExtractEntitySummariesFromEpisodes,
        ["classify_nodes"] = ClassifyNodes,
        ["extract_attributes"] = ExtractAttributes,
    };
}

internal abstract class ExtractNodesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "extract_message" => ExtractMessage,
            "extract_json" => ExtractJson,
            "extract_text" => ExtractText,
            "classify_nodes" => ClassifyNodes,
            "extract_attributes" => ExtractAttributes,
            "extract_summary" => ExtractSummary,
            "extract_summaries_batch" => ExtractSummariesBatch,
            "extract_entity_summaries_from_episodes" => ExtractEntitySummariesFromEpisodes,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt ExtractMessage(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractJson(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractText(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ClassifyNodes(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractAttributes(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractSummary(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractSummariesBatch(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt ExtractEntitySummariesFromEpisodes(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultExtractNodesPrompts : ExtractNodesPrompts
{
    // TODO(port): DefaultExtractNodesPrompts.extract_message
    internal override ChatPrompt ExtractMessage(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_message");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_json
    internal override ChatPrompt ExtractJson(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_json");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_text
    internal override ChatPrompt ExtractText(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_text");
    }

    // TODO(port): DefaultExtractNodesPrompts.classify_nodes
    internal override ChatPrompt ClassifyNodes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.classify_nodes");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_attributes
    internal override ChatPrompt ExtractAttributes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_attributes");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_summary
    internal override ChatPrompt ExtractSummary(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_summary");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_summaries_batch
    internal override ChatPrompt ExtractSummariesBatch(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_summaries_batch");
    }

    // TODO(port): DefaultExtractNodesPrompts.extract_entity_summaries_from_episodes
    internal override ChatPrompt ExtractEntitySummariesFromEpisodes(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultExtractNodesPrompts.extract_entity_summaries_from_episodes");
    }
}
