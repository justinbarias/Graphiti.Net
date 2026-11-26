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

using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

internal sealed record ExtractedEntity : IPydanticModel
{
    internal required string Name { get; set; }

    internal required long EntityTypeId { get; set; }

    internal List<long> EpisodeIndices { get; set; } = [0];

    internal ExtractedEntity Validate() => this;

    internal static ExtractedEntity ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ExtractedEntity", data);
        var name = f.Required("name", Pydantic.Str);
        var entityTypeId = f.Required("entity_type_id", Pydantic.Int);
        var episodeIndices = f.Default("episode_indices", v => Pydantic.List(v, Pydantic.Int), () => [0]);
        f.ThrowIfAny();
        return new ExtractedEntity { Name = name.Value, EntityTypeId = entityTypeId.Value, EpisodeIndices = episodeIndices.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("name", Name), ("entity_type_id", EntityTypeId), ("episode_indices", EpisodeIndices));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("ExtractedEntity")
        .Required("name", PydanticType.Str, "Name of the extracted entity")
        .Required("entity_type_id", PydanticType.Int, "ID of the classified entity type. Must be one of the provided entity_type_id integers.")
        .Factory("episode_indices", PydanticType.List(PydanticType.Int), "List of episode numbers (0-indexed) this entity was extracted from. When processing a single episode, this should be [0].");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("ExtractedEntity", ModelJsonSchema);
}

internal sealed record ExtractedEntities : IPydanticModel
{
    // PORT: the suffix Value (R2). C# does not allow a member named like its type (error CS0542), so `extracted_entities` is ExtractedEntitiesValue. The wire name stays `extracted_entities`.
    internal required List<ExtractedEntity> ExtractedEntitiesValue { get; set; }

    internal ExtractedEntities Validate() => this;

    internal static ExtractedEntities ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ExtractedEntities", data);
        var extractedEntities = f.Required("extracted_entities", v => Pydantic.List(v, x => Pydantic.Model(x, "ExtractedEntity", ExtractedEntity.ModelValidate)));
        f.ThrowIfAny();
        return new ExtractedEntities { ExtractedEntitiesValue = extractedEntities.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("extracted_entities", ExtractedEntitiesValue));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("ExtractedEntities")
        .Required("extracted_entities", PydanticType.List(PydanticType.Model(ExtractedEntity.ModelJsonSchema)), "List of extracted entities");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("ExtractedEntities", ModelJsonSchema);
}

internal sealed record EntitySummary : IPydanticModel
{
    internal required string Summary { get; set; }

    internal EntitySummary Validate() => this;

    internal static EntitySummary ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EntitySummary", data);
        var summary = f.Required("summary", Pydantic.Str);
        f.ThrowIfAny();
        return new EntitySummary { Summary = summary.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("summary", Summary));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EntitySummary")
        .Required("summary", PydanticType.Str, "Summary of the entity");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("EntitySummary", ModelJsonSchema);
}

internal sealed record SummarizedEntity : IPydanticModel
{
    internal required string Name { get; set; }

    internal required string Summary { get; set; }

    internal SummarizedEntity Validate() => this;

    internal static SummarizedEntity ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SummarizedEntity", data);
        var name = f.Required("name", Pydantic.Str);
        var summary = f.Required("summary", Pydantic.Str);
        f.ThrowIfAny();
        return new SummarizedEntity { Name = name.Value, Summary = summary.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("name", Name), ("summary", Summary));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SummarizedEntity")
        .Required("name", PydanticType.Str, "Name of the entity being summarized")
        .Required("summary", PydanticType.Str, "Updated summary for the entity");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("SummarizedEntity", ModelJsonSchema);
}

internal sealed record SummarizedEntities : IPydanticModel
{
    internal required List<SummarizedEntity> Summaries { get; set; }

    internal SummarizedEntities Validate() => this;

    internal static SummarizedEntities ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SummarizedEntities", data);
        var summaries = f.Required("summaries", v => Pydantic.List(v, x => Pydantic.Model(x, "SummarizedEntity", SummarizedEntity.ModelValidate)));
        f.ThrowIfAny();
        return new SummarizedEntities { Summaries = summaries.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("summaries", Summaries));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SummarizedEntities")
        .Required("summaries", PydanticType.List(PydanticType.Model(SummarizedEntity.ModelJsonSchema)), "List of entity summaries. Only include entities that need summary updates.");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("SummarizedEntities", ModelJsonSchema);
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
    internal static List<Message> ExtractMessage(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity extraction specialist for conversational messages. "
                    + "NEVER extract abstract concepts, feelings, or generic words.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
NEVER extract any of the following:
- Pronouns (you, me, I, he, she, they, we, us, it, them, him, her, this, that, those)
- Abstract concepts or feelings (joy, balance, growth, resilience, happiness, passion, motivation)
- Generic common nouns or bare object words (day, life, people, work, stuff, things, food, time,
  way, tickets, supplies, clothes, keys, gear)
- Generic media/content nouns unless uniquely identified in the node name itself (photo, pic, picture,
  image, video, post, story)
- Generic event/activity nouns unless uniquely identified in the node name itself (event, game, meeting,
  class, workshop, competition)
- Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
  team, office)
- Ambiguous bare nouns whose meaning depends on sentence context rather than the node name itself
- Sentence fragments or clauses ("what you really care about", "results of that effort")
- Adjectives or descriptive phrases ("amazing", "something different", "new hair color")
- Duplicate references to the same real-world entity. Extract each entity at most once per message,
  even if it appears multiple times or both as a speaker label and in the body text.
- Bare relational or kinship terms (dad, mom, mother, father, sister, brother, husband, wife,
  spouse, son, daughter, uncle, aunt, cousin, grandma, grandpa, friend, boss, teacher, neighbor,
  roommate) and bare animal/pet words (dog, cat, pet, puppy, kitten). These are too generic on
  their own. Instead, qualify them with the possessor: extract "Nisha's dad" not "dad",
  "Jordan's dog" not "dog".
- Bare generic objects that cannot be meaningfully qualified with a possessor, brand, or
  distinguishing detail (e.g., NEVER extract "supplies" from "I picked up some supplies")

Your task is to extract **entity nodes** that are **explicitly** mentioned in the CURRENT MESSAGE.
Pronoun references such as he/she/they or this/that/those should be disambiguated to the names of the
reference entities. Only extract distinct entities from the CURRENT MESSAGE.

<ENTITY TYPES>
{{Py.Str(context["entity_types"])}}
</ENTITY TYPES>

<PREVIOUS MESSAGES>
{{PromptHelpers.ToPromptJson(Py.Iter(context["previous_episodes"]).ToList())}}
</PREVIOUS MESSAGES>

<CURRENT MESSAGE>
{{Py.Str(context["episode_content"])}}
</CURRENT MESSAGE>

1. **Speaker Extraction**: Always extract the speaker (the part before the colon `:` in each dialogue line) as the first entity node.
   - If the speaker is mentioned again in the message, treat both mentions as a **single entity**.

2. **Entity Identification**:
   - Extract named entities and specific, concrete things that are **explicitly** mentioned in the CURRENT MESSAGE.
   - Only extract entities that are specific enough to be uniquely identifiable. Ask: "Could this have its own Wikipedia article or database entry, OR is it specific enough to distinguish from other items of the same category within this conversation?"
   - For objects, possessions, and physical items, extract when they are specific enough
     to distinguish from other items of the same category. SHOULD be extracted:
     - Brand-named items ("Gamecube", "Ford Mustang", "Moen faucet")
     - Qualified items ("wool coat", "red and purple lighting", "cracked windshield",
       "dog leash")
     - Items with a concrete distinguishing descriptor (color, material, size, model,
       owner, specific use)
     Should NOT be extracted:
     - Bare head nouns alone ("car", "coat", "game", "lighting", "windshield")
   - When a speaker or named person refers to a relative, pet, or associate using a bare term
     (e.g., "my dad", "his cat"), extract the entity qualified with the possessor's name
     (e.g., "Nisha's dad", "Jordan's cat"). Do NOT extract the bare term alone.
   - **Exclude** entities mentioned only in the PREVIOUS MESSAGES (they are for context only).

3. **Entity Classification**:
   - Use the descriptions in ENTITY TYPES to classify each extracted entity.
   - Assign the appropriate `entity_type_id` for each one.

4. **Exclusions**:
   - Do NOT extract entities representing relationships or actions.
   - Do NOT extract dates, times, or other temporal information — these will be handled separately.
   - When in doubt, do NOT extract.

5. **Specificity**:
   - Always use the **most specific form** mentioned in the message. If the message says "road cycling",
     extract "road cycling" not "cycling". If it says "wool coat", extract "wool coat" not "coat".
   - When context makes an object's type clear, include that context in the name. For example, if the
     message mentions forgetting a leash while discussing a dog walk, extract "dog leash" not "leash".
   - If a phrase would not be distinguishable when read alone later, do NOT extract it.

6. **Formatting**:
   - Be **explicit and unambiguous** in naming entities (e.g., use full names when available).

<EXAMPLE>
Message: "Jordan: We just moved to Denver last month. My spouse started a new role at Lockheed Martin and I enrolled in a ceramics workshop at the Belmont Arts Center."
Good extractions: "Jordan" (speaker), "Denver" (Location), "Lockheed Martin" (Organization), "Belmont Arts Center" (Location), "ceramics" (Topic)
Do NOT extract: "spouse" (generic reference — extract only if named), "new role" (not an entity), "last month" (temporal), "we" (pronoun)
</EXAMPLE>

<EXAMPLE>
Message: "Nisha: My dad is visiting next week. He loves walking his dogs in Riverside Park."
Good extractions: "Nisha" (speaker), "Nisha's dad" (Person), "Riverside Park" (Location)
Do NOT extract: "dad" (bare relational term — qualify as "Nisha's dad"), "dogs" (bare animal word — no specific identity), "next week" (temporal)
</EXAMPLE>

<EXAMPLE>
Message: "Mary: I forgot Trigger's leash so I couldn't take him on a dog walk. After that I went road cycling in my new wool coat."
Good extractions: "Mary" (speaker), "Trigger" (animal name), "dog leash" (Object), "road cycling" (Topic), "wool coat" (Object)
Do NOT extract: "leash" (too generic — use "dog leash"), "cycling" (too generic — use "road cycling"), "coat" (too generic — use "wool coat"), "dog walk" (activity, not an entity)
</EXAMPLE>

<EXAMPLE>
Message: "Nate: My gaming room has red and purple lighting and I mostly play on a Gamecube. Last week the windshield on my Mustang got cracked."
Good extractions: "Nate" (speaker), "gaming room" (Object), "red and purple lighting" (Object), "Gamecube" (Object), "Mustang" (Object), "cracked windshield" (Object)
Do NOT extract: "lighting" (bare head noun — use "red and purple lighting"), "windshield" (bare head noun — use "cracked windshield"), "week" (temporal)
</EXAMPLE>

<EXAMPLE>
Message: "Alex: I shared a pic from the game after the event."
Good extractions: "Alex" (speaker)
Do NOT extract: "pic" (generic media noun), "game" (generic event noun), "event" (generic event noun)
</EXAMPLE>

<EXAMPLE>
Message: "Jordan: We won by a tight score. Scoring that last basket felt incredible."
Good extractions: "Jordan" (speaker)
Do NOT extract: "basket" (ambiguous bare noun that depends on sentence context)
</EXAMPLE>

{{Py.Str(context["custom_extraction_instructions"])}}
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> ExtractJson(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity extraction specialist for JSON data. "
                    + "NEVER extract abstract concepts, dates, or generic field values.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
NEVER extract:
- Date, time, or timestamp values
- Abstract concepts or generic field values (e.g., "true", "active", "pending")
- Numeric IDs or codes that are not meaningful entity names
- Bare relational or kinship terms (e.g., "spouse", "parent", "pet") — only extract if qualified
  with a possessor name
- Bare generic objects or common nouns (e.g., "supplies", "tickets", "gear") — only extract if
  qualified with a distinguishing detail
- Generic media/content nouns unless uniquely identified in the value itself (photo, pic, picture,
  image, video, post, story)
- Generic event/activity nouns unless uniquely identified in the value itself (event, game, meeting,
  class, workshop, competition)
- Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
  team, office)
- Ambiguous bare nouns whose meaning depends on surrounding text rather than the extracted value itself

Extract entities from the JSON and classify each using the ENTITY TYPES above.

<ENTITY TYPES>
{{Py.Str(context["entity_types"])}}
</ENTITY TYPES>

<SOURCE DESCRIPTION>
{{Py.Str(context["source_description"])}}
</SOURCE DESCRIPTION>

<JSON>
{{Py.Str(context["episode_content"])}}
</JSON>

Guidelines:
1. Extract the primary entity the JSON represents (e.g., a "name" or "user" field).
2. Extract named entities referenced in other properties throughout the JSON structure.
3. Only extract entities specific enough to be uniquely identifiable.
4. Be explicit in naming entities — use full names when available.
5. Use the most specific form present in the data (e.g., "road cycling" not "cycling").
6. If a value would not be meaningful and distinguishable when read alone later, do NOT extract it.

{{Py.Str(context["custom_extraction_instructions"])}}

<EXAMPLE>
JSON: {"user": "Jordan Lee", "company": "Acme Corp", "role": "engineer", "start_date": "2024-01-15", "location": "Denver", "active": true}
Good extractions: "Jordan Lee" (Person), "Acme Corp" (Organization), "Denver" (Location)
Do NOT extract: "engineer" (role, not an entity), "2024-01-15" (date), "true" (field value)
</EXAMPLE>

<EXAMPLE>
JSON: {"author": "Alex", "attachment_type": "photo", "event_name": "event", "agency": "government"}
Good extractions: "Alex" (Person)
Do NOT extract: "photo" (generic media noun), "event" (generic event noun), "government" (broad institutional noun)
</EXAMPLE>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> ExtractText(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity extraction specialist for unstructured text. "
                    + "NEVER extract abstract concepts, feelings, or generic words.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
NEVER extract:
- Pronouns (you, me, he, she, they, it, them, him, her, we, us, this, that, those)
- Abstract concepts (joy, balance, growth, resilience, passion, motivation)
- Generic common nouns or bare object words (day, life, people, work, stuff, things, food, time,
  tickets, supplies, clothes, keys, gear)
- Generic media/content nouns unless uniquely identified in the node name itself (photo, pic, picture,
  image, video, post, story)
- Generic event/activity nouns unless uniquely identified in the node name itself (event, game, meeting,
  class, workshop, competition)
- Broad institutional nouns unless explicitly named or uniquely qualified (government, school, company,
  team, office)
- Ambiguous bare nouns whose meaning depends on sentence context rather than the node name itself
- Sentence fragments or clauses as entity names
- Bare relational or kinship terms (dad, mom, sister, brother, spouse, friend, boss, pet, dog,
  cat) unless qualified with a possessor (e.g., "Nisha's dad" is acceptable, "dad" alone is not)
- Bare generic objects that cannot be meaningfully qualified with a possessor, brand, or
  distinguishing detail (e.g., NEVER extract "supplies" from "I picked up some supplies")

Extract entities from the TEXT that are **explicitly mentioned**.
For each entity, classify it using the ENTITY TYPES above.
Only extract entities specific enough to be uniquely identifiable — ask: "Could this have its own Wikipedia article or database entry?"

<ENTITY TYPES>
{{Py.Str(context["entity_types"])}}
</ENTITY TYPES>

<TEXT>
{{Py.Str(context["episode_content"])}}
</TEXT>

Guidelines:
1. Extract named entities and specific, concrete things.
2. Do not create nodes for relationships or actions.
3. Do not create nodes for temporal information like dates, times or years.
4. Be explicit in node names, using full names and avoiding abbreviations.
5. Always use the most specific form from the text (e.g., "road cycling" not "cycling",
   "wool coat" not "coat"). Include qualifying context when it's clear from the text.
6. When the text refers to a person's relative, pet, or associate by a bare term, qualify the
   entity with the possessor's name (e.g., "Dr. Osei's colleague" not "colleague").
7. If a phrase would not be meaningful and distinguishable when read alone later, do NOT extract it.
8. When in doubt, do NOT extract.

{{Py.Str(context["custom_extraction_instructions"])}}

<EXAMPLE>
Text: "Dr. Amara Osei presented her migraine study results at the AAN conference. The study tracked 340 patients using a new CGRP combination protocol."
Good extractions: "Dr. Amara Osei" (Person), "AAN" (Organization), "migraine study" (Topic), "CGRP combination protocol" (Object)
Do NOT extract: "results" (generic noun), "340" (number), "patients" (generic noun), "conference" (generic without a specific name)
</EXAMPLE>

<EXAMPLE>
Text: "Alex shared a pic after the event and said scoring the last basket felt incredible."
Good extractions: "Alex" (Person)
Do NOT extract: "pic" (generic media noun), "event" (generic event noun), "basket" (ambiguous bare noun)
</EXAMPLE>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> ClassifyNodes(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity classification specialist. "
                    + "NEVER assign types not listed in ENTITY TYPES.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
<PREVIOUS MESSAGES>
{{PromptHelpers.ToPromptJson(Py.Iter(context["previous_episodes"]).ToList())}}
</PREVIOUS MESSAGES>

<CURRENT MESSAGE>
{{Py.Str(context["episode_content"])}}
</CURRENT MESSAGE>

<EXTRACTED ENTITIES>
{{Py.Str(context["extracted_entities"])}}
</EXTRACTED ENTITIES>

<ENTITY TYPES>
{{Py.Str(context["entity_types"])}}
</ENTITY TYPES>

Given the above conversation, extracted entities, and provided entity types and their descriptions, classify the extracted entities.

Guidelines:
1. Each entity must have exactly one type.
2. NEVER use types not listed in ENTITY TYPES.
3. If none of the provided entity types accurately classify an extracted entity, the type should be set to None.
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> ExtractAttributes(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity attribute extraction specialist. "
                    + "You ONLY emit attribute values that are explicitly stated in MESSAGES or "
                    + "already present on the ENTITY. You output strictly the JSON specified by the "
                    + "response schema — no reasoning, no explanation, no commentary in any field.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = $$"""
Given the MESSAGES and the following ENTITY, update its attributes.

HARD RULES — violating any of these is a failure:

1. Each attribute value MUST be one of:
   (a) a clean value copied or directly normalized from text in MESSAGES,
   (b) the existing value already on the ENTITY (preserved unchanged), or
   (c) null / omitted, when neither (a) nor (b) applies.

2. NEVER write reasoning, justification, or commentary into any field. Specifically:
   - NEVER include parenthetical explanations like "(implied by ...)", "(Context: ...)",
     "(not explicitly stated ...)", "(based on ...)".
   - NEVER include first-person or deliberative phrases like "I should...", "However...",
     "Sticking to...", "Since no...", "the instruction is to...", "must be kept...",
     "if no value is present...".
   - NEVER list alternatives or candidates inside one field ("X, or Y, or maybe Z").
   - NEVER explain why a value is null. If unknown, set the field to null and stop.

3. Each attribute schema description (e.g. an "Industry sector" field whose description
   reads "Industry classification, single word where possible") tells you the FORMAT a
   real value should take. The description text is NEVER itself a value. NEVER copy
   schema description text into the field.

4. The literal strings "null", "N/A", "Not specified", "unknown", "none", "not provided",
   or any sentence describing absence are NOT valid values. If no value is supported by
   MESSAGES, set the field to null (or omit it) — do not write a sentence.

5. Each attribute value must be a short, well-formed instance of the type the field
   describes (a phone number, an industry name, a URL, a postal address). If you cannot
   produce a clean value of that type from MESSAGES, the field is null.

6. NEVER infer attribute values from the entity's name, from related entities, from
   generic world knowledge, or from prior summaries. Only verbatim or directly normalized
   text from MESSAGES qualifies as a new value.
   Exception: for name-part fields such as first_name, last_name, given_name, or family_name,
   a full person name explicitly present in MESSAGES is sufficient evidence to extract the
   corresponding part. Do not use ENTITY.name alone unless the same name also appears in MESSAGES.

7. If MESSAGES contain no information about an attribute, leave the existing entity
   value unchanged. If the entity has no existing value, the field is null.

EXAMPLES

ENTITY: {"name": "Sam Rivera", "phones": "415-555-0142"}
MESSAGES contain no phone information for Sam.
GOOD → "phones": "415-555-0142"   (preserved existing value)
BAD  → "phones": "415-555-0142 (implied by original entity, but no new information in
        messages, retaining original value as per instruction...)"

ENTITY: {"name": "Northwind", "industry": null}
MESSAGES mention Northwind only as the platform some content was posted to.
GOOD → "industry": null   (no explicit industry classification was stated)
BAD  → "industry": "Content platform, SaaS (implied by usage context, though not stated
        explicitly as industry classification...)"

ENTITY: {"name": "Priya"}
MESSAGES contain no phone for Priya, but discuss a project she contributed to.
GOOD → "phones": null
BAD  → "phones": "Worked with Lin and Marco on the Q3 launch..."   (off-topic content dump)

<MESSAGES>
{{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
{{PromptHelpers.ToPromptJson(context["episode_content"])}}
</MESSAGES>

<ENTITY>
{{Py.Str(context["node"])}}
</ENTITY>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> ExtractSummary(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are a helpful assistant that extracts entity summaries from the provided text.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
        Given the MESSAGES and the ENTITY, update the summary that combines relevant information about the entity
        from the messages and relevant information from the existing summary. Summary must be under {{Py.Str(TextUtils.MaxSummaryChars)}} characters.

        {{Snippets.SummaryInstructions}}

        <MESSAGES>
        {{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
        {{PromptHelpers.ToPromptJson(context["episode_content"])}}
        </MESSAGES>

        <ENTITY>
        {{Py.Str(context["node"])}}
        </ENTITY>
""" + "\n        ",
            }.Validate(),
        ];
    }

    // PORT: internal, because judge cases call it (R2).
    /// <summary>Build an optional prompt section with entity type descriptions.</summary>
    internal static string EntityTypeDescriptionsSection(OrderedDictionary<string, object?> context)
    {
        var descriptions = Py.Get(context, "entity_type_descriptions");
        if (!Py.Truthy(descriptions))
        {
            return "";
        }

        return "\n" + $$"""
<ENTITY_TYPE_DESCRIPTIONS>
{{PromptHelpers.ToPromptJson(descriptions)}}
</ENTITY_TYPE_DESCRIPTIONS>
When an entity's type appears in ENTITY_TYPE_DESCRIPTIONS, use the description to decide which facts are most relevant to that entity type. NEVER mention the entity type, type description, or classification in the summary text itself.
""" + "\n";
    }

    internal static List<Message> ExtractSummariesBatch(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are a helpful assistant that generates concise entity summaries from provided context.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
Given the MESSAGES and a list of ENTITIES, generate an updated summary for each entity that needs one.
Each summary must be under {{Py.Str(TextUtils.MaxSummaryChars)}} characters.

{{Snippets.SummaryInstructions}}

<MESSAGES>
{{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
{{PromptHelpers.ToPromptJson(context["episode_content"])}}
</MESSAGES>
{{EntityTypeDescriptionsSection(context)}}
<ENTITIES>
{{PromptHelpers.ToPromptJson(context["entities"])}}
</ENTITIES>

For each entity, combine relevant information from the MESSAGES with any existing summary content.
Only return summaries for entities that have meaningful information to summarize.
If an entity has no relevant information in the messages and no existing summary, you may skip it.
""" + "\n",
            }.Validate(),
        ];
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
    internal static List<Message> ExtractEntitySummariesFromEpisodes(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = EntityEpisodeSummarySystemPrompt,
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = $$"""
NEVER include meta-language about the summarization process. Use ONLY facts from the provided EPISODES.
Each summary must be under {{Py.Str(TextUtils.MaxSummaryChars)}} characters. Write 2-6 dense sentences in third person. Preserve all material names, roles, dates, counts, and changes over time that are explicitly supported.

For each entity below, generate an updated summary using ONLY the provided EPISODES and any existing summary already on the entity.

<EPISODES>
{{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
{{PromptHelpers.ToPromptJson(context["episode_content"])}}
</EPISODES>
{{EntityTypeDescriptionsSection(context)}}
<ENTITIES>
{{PromptHelpers.ToPromptJson(context["entities"])}}
</ENTITIES>

Only return summaries for entities that have meaningful information to summarize.
If an entity has no relevant information in the episodes and no existing summary, you may skip it.
""" + "\n",
            }.Validate(),
        ];
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
    internal override ChatPrompt ExtractMessage(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractMessage(context));

    internal override ChatPrompt ExtractJson(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractJson(context));

    internal override ChatPrompt ExtractText(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractText(context));

    internal override ChatPrompt ClassifyNodes(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ClassifyNodes(context));

    internal override ChatPrompt ExtractAttributes(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractAttributes(context));

    internal override ChatPrompt ExtractSummary(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractSummary(context));

    internal override ChatPrompt ExtractSummariesBatch(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractSummariesBatch(context));

    internal override ChatPrompt ExtractEntitySummariesFromEpisodes(OrderedDictionary<string, object?> context)
    =>
        ChatPrompt.FromMessages(ExtractNodes.ExtractEntitySummariesFromEpisodes(context));
}
