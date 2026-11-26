// Ported from graphiti_core/prompts/extract_nodes_and_edges.py
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

using Graphiti.Internal;
using Graphiti.PyCompat;

/// <summary>Entity extracted by the combined node+edge extraction prompt.</summary>
internal sealed record CombinedEntity : IPydanticModel
{
    internal required string Name { get; set; }

    internal required long EntityTypeId { get; set; }

    internal CombinedEntity Validate() => this;

    internal static CombinedEntity ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CombinedEntity", data);
        var name = f.Required("name", Pydantic.Str);
        var entityTypeId = f.Required("entity_type_id", Pydantic.Int);
        f.ThrowIfAny();
        return new CombinedEntity { Name = name.Value, EntityTypeId = entityTypeId.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("name", Name), ("entity_type_id", EntityTypeId));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CombinedEntity", "Entity extracted by the combined node+edge extraction prompt.")
        .Required("name", PydanticType.Str, "Name of the extracted entity")
        .Required("entity_type_id", PydanticType.Int, "ID of the classified entity type. " + "Must be one of the provided entity_type_id integers.");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("CombinedEntity", ModelJsonSchema);
}

/// <summary>Relationship fact extracted by the combined node+edge extraction prompt.</summary>
internal sealed record CombinedFact : IPydanticModel
{
    internal required string SourceEntityName { get; set; }

    internal required string TargetEntityName { get; set; }

    internal required string RelationType { get; set; }

    internal required string Fact { get; set; }

    internal List<long> EpisodeIndices { get; set; } = [0];

    internal CombinedFact Validate() => this;

    internal static CombinedFact ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CombinedFact", data);
        var sourceEntityName = f.Required("source_entity_name", Pydantic.Str);
        var targetEntityName = f.Required("target_entity_name", Pydantic.Str);
        var relationType = f.Required("relation_type", Pydantic.Str);
        var fact = f.Required("fact", Pydantic.Str);
        var episodeIndices = f.Default("episode_indices", v => Pydantic.List(v, Pydantic.Int), () => [0]);
        f.ThrowIfAny();
        return new CombinedFact
        {
            SourceEntityName = sourceEntityName.Value,
            TargetEntityName = targetEntityName.Value,
            RelationType = relationType.Value,
            Fact = fact.Value,
            EpisodeIndices = [.. episodeIndices.Value],
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("source_entity_name", SourceEntityName),
        ("target_entity_name", TargetEntityName),
        ("relation_type", RelationType),
        ("fact", Fact),
        ("episode_indices", EpisodeIndices));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CombinedFact", "Relationship fact extracted by the combined node+edge extraction prompt.")
        .Required("source_entity_name", PydanticType.Str, "The name of the source entity. MUST exactly match an entity `name` in " + "extracted_entities (including value-like endpoints such as versions).")
        .Required("target_entity_name", PydanticType.Str, "The name of the target entity. MUST exactly match an entity `name` in " + "extracted_entities (including value-like endpoints such as versions).")
        .Required("relation_type", PydanticType.Str, "The type of relationship between the entities, in SCREAMING_SNAKE_CASE " + "(e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH)")
        .Required("fact", PydanticType.Str, "A self-contained natural language description of the relationship, " + "paraphrased from the source text with all specific details preserved")
        .Factory("episode_indices", PydanticType.List(PydanticType.Int), "List of episode numbers (0-indexed) that this fact was derived from. " + "When processing a single episode, this should be [0].");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("CombinedFact", ModelJsonSchema);
}

/// <summary>Combined node and edge extraction response.</summary>
internal sealed record CombinedExtraction : IPydanticModel
{
    internal required List<CombinedEntity> ExtractedEntities { get; set; }

    internal required List<CombinedFact> Edges { get; set; }

    internal CombinedExtraction Validate() => this;

    internal static CombinedExtraction ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CombinedExtraction", data);
        var extractedEntities = f.Required("extracted_entities", v => Pydantic.List(v, x => Pydantic.Model(x, "CombinedEntity", CombinedEntity.ModelValidate)));
        var edges = f.Required("edges", v => Pydantic.List(v, x => Pydantic.Model(x, "CombinedFact", CombinedFact.ModelValidate)));
        f.ThrowIfAny();
        return new CombinedExtraction { ExtractedEntities = [.. extractedEntities.Value], Edges = [.. edges.Value] }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("extracted_entities", ExtractedEntities), ("edges", Edges));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CombinedExtraction", "Combined node and edge extraction response.")
        .Required("extracted_entities", PydanticType.List(PydanticType.Model(CombinedEntity.ModelJsonSchema)), "List of extracted entities")
        .Required("edges", PydanticType.List(PydanticType.Model(CombinedFact.ModelJsonSchema)), "List of extracted relationship facts");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("CombinedExtraction", ModelJsonSchema);
}

internal interface ExtractNodesAndEdgesPrompt
{
    PromptFunction ExtractMessage { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class ExtractNodesAndEdges
{
    // PORT: `list[dict]` is a list of the dict of the value model (R6). Internal, because judge cases call it (R2).
    // PORT: the body is BuildEdgeTypesSectionOf. It takes the open values of a context dict, because Python gets any
    // value there and the typed signature cannot hold it. (No rule: reported.)
    internal static string BuildEdgeTypesSection(List<OrderedDictionary<string, object?>>? edgeTypes, bool strictEdgeTypes = false) =>
        BuildEdgeTypesSectionOf(edgeTypes, strictEdgeTypes);

    private static string BuildEdgeTypesSectionOf(object? edgeTypes, object? strictEdgeTypes)
    {
        if (!Py.Truthy(edgeTypes))
        {
            return string.Empty;
        }

        var relationTypeRules = Py.Truthy(strictEdgeTypes)
            ? "- Use one of the provided fact_type_name values as the relation_type.\n"
                + "- If a relationship does not match any FACT TYPE, skip it. Do not derive a new "
                + "relation_type."
            : "- When a relationship matches a FACT TYPE, use that fact_type_name as the "
                + "relation_type.\n"
                + "- If no FACT TYPE fits, derive a relation_type in SCREAMING_SNAKE_CASE\n"
                + "  (e.g., WORKS_AT, LIVES_IN, IS_FRIENDS_WITH).";
        return "\n" + $$"""
<FACT TYPES>
{{PromptHelpers.ToPromptJson(edgeTypes)}}
</FACT TYPES>
RELATION TYPE RULES:
{{relationTypeRules}}
""" + "\n";
    }

    // PORT: internal, because judge cases call it (R2). The body is BuildCustomInstructionsSectionOf, for the open
    // value of a context dict.
    internal static string BuildCustomInstructionsSection(string? customExtractionInstructions) =>
        BuildCustomInstructionsSectionOf(customExtractionInstructions);

    private static string BuildCustomInstructionsSectionOf(object? customExtractionInstructions)
    {
        if (!Py.Truthy(customExtractionInstructions))
        {
            return string.Empty;
        }

        return "\n" + $$"""
<CUSTOM INSTRUCTIONS>
{{Py.Str(customExtractionInstructions)}}
</CUSTOM INSTRUCTIONS>
CUSTOM INSTRUCTION RULES:
- CUSTOM INSTRUCTIONS come from the owner of this graph. Apply them. When a CUSTOM INSTRUCTION conflicts with a rule above, the CUSTOM INSTRUCTION wins.
""" + "\n";
    }

    internal const string SystemPrompt = "You build the long-term memory of an AI agent. From each conversation you extract the entities and the relationship facts that the agent will need later to answer questions, personalize responses, and continue work. The original conversation will not be available later: only your entities and facts will remain, so each fact must stand alone.";
    private const string C2InstructionText = """
# Goal
Turn CURRENT MESSAGES into a knowledge graph: a list of entities and a list of facts (edges) between them. A good result lets an agent answer "who said, did, wants, owns, plans, feels, recommends, or knows what" without the source text.

# What good output looks like
- Complete: every episode, post, and speaker with real content is covered, including the later ones in a long input. Preferences, opinions, plans, commitments, requests, questions, emotions, experiences, advice, and results all count. Skip only content-free turns ("Hi!", "Thanks!", "Bye!").
- Attributed: when a named person, author, account, or organization states, recommends, or does something, that actor is the source entity, and the fact text names the actor.
- Self-contained: each fact is one plain sentence that uses entity names (no pronouns), keeps the specific details (numbers, dates, prices, versions, names), and makes sense with no other context.
- Compact: one fact for one statement. Keep a list of items from one statement in one fact ("Marina plans to change into a sweater and trousers."), unless each item has its own details. Do not make a fact or an entity for each list item. Not redundant: If the same fact appears in more than one episode, emit it once and list all of its episode indices.

# Entities
- Speakers. Name each speaker with the most complete name available. When a message header has profile fields (first_name and last_name, given_name and family_name, user_alias, screen_name), use the full personal name, for example "Marisol Beckett". Use "User" only when no name is available.
- Other entities: named people, organizations, places, products, files, tickets, projects, events, documents, and named or described objects, plus hobbies and activities that someone does ("road cycling", "watercolor painting"). Use the most specific form mentioned, at most 5 words, as a noun phrase. For a person's possession, pet, project, or creation, use a possessive name ("Calvin's guitar", "Marina's sweater"), not the bare noun.
- Do not make entities from: pronouns or unresolved references ("the city", "this issue"); vague abstractions ("growth", "life", "time"); clock times, dates, quantities, prices, durations, measurements, or coordinates (keep these in the fact text, unless the value is itself an edge endpoint such as a version string); answer options or template tokens ("Agree", "Strongly disagree"); imperative tips ("Buy in bulk" becomes the topic "bulk buying"); quoted slogans or idioms; tutorial example values ("/path/to/source", "file.txt").
- Each entity appears once. Classify it with an entity_type_id from ENTITY TYPES. Extract entities only from CURRENT MESSAGES; use PREVIOUS MESSAGES and [CONTEXT EPISODE] messages only to resolve references.

# Facts
- source_entity_name and target_entity_name must be names from extracted_entities. If an endpoint is a value such as a version, add it as an entity.
- Connect the actor directly to the thing: "Calvin -> PERFORMED_IN -> Tokyo", "Mira Sen -> RECOMMENDS_METRICS_FOR -> Ember & Oak". Do not route a relationship through descriptive nouns ("city lights -> LOCATED_IN -> Tokyo"), and do not turn an actor's claim into a fact between two nouns that the actor mentioned.
- Use a self-referencing fact (source and target are the same entity) only when a statement is about one entity and no other extracted entity fits, for example a routine, a feeling, or a health state ("Evan has a knee injury.").
- When a fact involves two extracted entities, use both as source and target.
- Use PREVIOUS MESSAGES and [CONTEXT EPISODE] messages to resolve what the current message refers to, and write the resolved fact ("all the hard work paid off" becomes what paid off). [CONTEXT EPISODE] messages are conversation turns between the numbered episodes, shown so you can read the conversation in order.
- Set episode_indices to the 0-based [Episode N] numbers that the fact comes from. Messages headed [CONTEXT EPISODE] are not numbered episodes: do not extract facts from them and do not give them an episode index.
- Write each entity name and each fact in the dominant language of its source episode. Keep proper names as written. Write relation_type as an English SCREAMING_SNAKE_CASE label.
- A fact states what the source says. Do not add reasoning, hedges ("appears to", "suggests"), or commentary.

# Output
Return only the JSON object: {"extracted_entities":[{"name":"...","entity_type_id":0}],"edges":[{"source_entity_name":"...","target_entity_name":"...","relation_type":"...","fact":"...","episode_indices":[0]}]}.
""";

    private static string BuildExtractUserPrompt(OrderedDictionary<string, object?> context)
    {
        return $$"""
{{C2InstructionText}}

<ENTITY TYPES>
{{Py.Str(context["entity_types"])}}
</ENTITY TYPES>
{{BuildEdgeTypesSectionOf(Py.Get(context, "edge_types"), Py.Get(context, "strict_edge_types", false))}}
{{BuildCustomInstructionsSectionOf(Py.Get(context, "custom_extraction_instructions"))}}
<PREVIOUS MESSAGES>
{{PromptHelpers.ToPromptJson(Py.Iter(context["previous_episodes"]).ToList())}}
</PREVIOUS MESSAGES>

<CURRENT MESSAGES>
{{Py.Str(context["episode_content"])}}
</CURRENT MESSAGES>
""" + "\n";
    }

    internal static List<Message> ExtractMessage(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message { Role = "system", Content = SystemPrompt }.Validate(),
            new Message { Role = "user", Content = BuildExtractUserPrompt(context) }.Validate(),
        ];
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["extract_message"] = ExtractMessage,
    };
}

internal abstract class ExtractNodesAndEdgesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "extract_message" => ExtractMessage,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt ExtractMessage(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultExtractNodesAndEdgesPrompts : ExtractNodesAndEdgesPrompts
{
    internal override ChatPrompt ExtractMessage(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(ExtractNodesAndEdges.ExtractMessage(context));
}
