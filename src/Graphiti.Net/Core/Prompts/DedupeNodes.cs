// Ported from graphiti_core/prompts/dedupe_nodes.py
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

internal sealed record NodeDuplicate : IPydanticModel
{
    internal required long Id { get; set; }

    internal required string Name { get; set; }

    internal required long DuplicateCandidateId { get; set; }

    internal NodeDuplicate Validate() => this;

    internal static NodeDuplicate ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeDuplicate", data);
        var id = f.Required("id", Pydantic.Int);
        var name = f.Required("name", Pydantic.Str);
        var duplicateCandidateId = f.Required("duplicate_candidate_id", Pydantic.Int);
        f.ThrowIfAny();
        return new NodeDuplicate { Id = id.Value, Name = name.Value, DuplicateCandidateId = duplicateCandidateId.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("id", Id), ("name", Name), ("duplicate_candidate_id", DuplicateCandidateId));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("NodeDuplicate")
        .Required("id", PydanticType.Int, "integer id of the entity")
        .Required("name", PydanticType.Str, "Name of the entity. Should be the most complete and descriptive name of the entity. Do not include any JSON formatting in the Entity name such as {}.")
        .Required("duplicate_candidate_id", PydanticType.Int, "candidate_id of the matching EXISTING ENTITY, or -1 if no duplicate exists.");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("NodeDuplicate", ModelJsonSchema);
}

internal sealed record NodeResolutions : IPydanticModel
{
    internal required List<NodeDuplicate> EntityResolutions { get; set; }

    internal NodeResolutions Validate() => this;

    internal static NodeResolutions ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeResolutions", data);
        var entityResolutions = f.Required("entity_resolutions", v => Pydantic.List(v, x => Pydantic.Model(x, "NodeDuplicate", NodeDuplicate.ModelValidate)));
        f.ThrowIfAny();
        return new NodeResolutions { EntityResolutions = entityResolutions.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("entity_resolutions", EntityResolutions));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("NodeResolutions")
        .Required("entity_resolutions", PydanticType.List(PydanticType.Model(NodeDuplicate.ModelJsonSchema)), "List of resolved nodes");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("NodeResolutions", ModelJsonSchema);
}

internal interface DedupeNodesPrompt
{
    PromptFunction Node { get; }

    PromptFunction NodeList { get; }

    PromptFunction Nodes { get; }
}

/// <summary>The module-level functions and the <c>versions</c> table of <c>prompts/dedupe_nodes.py</c>.</summary>
internal static class DedupeNodes
{
    internal static List<Message> Node(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity deduplication assistant. "
                    + "NEVER fabricate entity names or mark distinct entities as duplicates.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
<PREVIOUS MESSAGES>
{{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
</PREVIOUS MESSAGES>

<CURRENT MESSAGE>
{{Py.Str(context["episode_content"])}}
</CURRENT MESSAGE>

<NEW ENTITY>
{{PromptHelpers.ToPromptJson(context["extracted_node"])}}
</NEW ENTITY>

<ENTITY TYPE DESCRIPTION>
{{PromptHelpers.ToPromptJson(context["entity_type_description"])}}
</ENTITY TYPE DESCRIPTION>

<EXISTING ENTITIES>
{{PromptHelpers.ToPromptJson(context["existing_nodes"])}}
</EXISTING ENTITIES>

Entities should only be considered duplicates if they refer to the *same real-world object or concept*.
Semantic Equivalence: if a descriptive label in EXISTING ENTITIES clearly refers to a named entity in context, treat them as duplicates.

NEVER mark entities as duplicates if:
- They are related but distinct.
- They have similar names or purposes but refer to separate instances or concepts.

Task:
1. Compare the NEW ENTITY against each EXISTING ENTITY (identified by `candidate_id`).
2. If it refers to the same real-world object or concept, return the `candidate_id` of that match.
3. Return `duplicate_candidate_id = -1` when there is no match or you are unsure.

<EXAMPLE>
NEW ENTITY: "Sam" (Person)
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Sam", "entity_types": ["Person"], "summary": "Sam enjoys hiking and photography"}]
Result: duplicate_candidate_id = 0 (same person referenced in conversation)

NEW ENTITY: "NYC"
EXISTING ENTITIES: [{"candidate_id": 0, "name": "New York City", "entity_types": ["Location"]}, {"candidate_id": 1, "name": "New York Knicks", "entity_types": ["Organization"]}]
Result: duplicate_candidate_id = 0 (same location, abbreviated name)

NEW ENTITY: "Java" (programming language)
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Java", "entity_types": ["Location"], "summary": "An island in Indonesia"}]
Result: duplicate_candidate_id = -1 (same name but distinct real-world things)

NEW ENTITY: "Marco's car"
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Marco's vehicle", "entity_types": ["Entity"], "summary": "Marco drives a red sedan."}]
Result: duplicate_candidate_id = 0 (synonym — "car" and "vehicle" refer to the same thing, same possessor)
</EXAMPLE>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> Nodes(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity deduplication assistant. "
                    + "NEVER fabricate entity names or mark distinct entities as duplicates.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
<PREVIOUS MESSAGES>
{{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
</PREVIOUS MESSAGES>

<CURRENT MESSAGE>
{{Py.Str(context["episode_content"])}}
</CURRENT MESSAGE>

<ENTITIES>
{{PromptHelpers.ToPromptJson(context["extracted_nodes"])}}
</ENTITIES>

<EXISTING ENTITIES>
{{PromptHelpers.ToPromptJson(context["existing_nodes"])}}
</EXISTING ENTITIES>

Each of the above ENTITIES was extracted from the CURRENT MESSAGE.
For each entity, determine if it is a duplicate of any EXISTING ENTITY.
Entities should only be considered duplicates if they refer to the *same real-world object or concept*.

NEVER mark entities as duplicates if:
- They are related but distinct.
- They have similar names or purposes but refer to separate instances or concepts.

Task:
ENTITIES contains {{Py.Str(Py.Len(context["extracted_nodes"]))}} entities with IDs 0 through {{Py.Str(Py.Len(context["extracted_nodes"]) - 1)}}.
Your response MUST include EXACTLY {{Py.Str(Py.Len(context["extracted_nodes"]))}} resolutions with IDs 0 through {{Py.Str(Py.Len(context["extracted_nodes"]) - 1)}}. Do not skip or add IDs.

For every entity, provide:
- `id`: integer id from ENTITIES
- `name`: the best full name for the entity (preserve the original name unless a duplicate has a more complete name)
- `duplicate_candidate_id`: the `candidate_id` of the EXISTING ENTITY that is the best duplicate match, or -1 if there is no duplicate

<EXAMPLE>
ENTITY: "Sam" (Person)
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Sam", "entity_types": ["Person"], "summary": "Sam enjoys hiking and photography"}]
Result: duplicate_candidate_id = 0 (same person referenced in conversation)

ENTITY: "NYC"
EXISTING ENTITIES: [{"candidate_id": 0, "name": "New York City", "entity_types": ["Location"]}, {"candidate_id": 1, "name": "New York Knicks", "entity_types": ["Organization"]}]
Result: duplicate_candidate_id = 0 (same location, abbreviated name)

ENTITY: "Java" (programming language)
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Java", "entity_types": ["Location"], "summary": "An island in Indonesia"}]
Result: duplicate_candidate_id = -1 (same name but distinct real-world things)

ENTITY: "Marco's car"
EXISTING ENTITIES: [{"candidate_id": 0, "name": "Marco's vehicle", "entity_types": ["Entity"], "summary": "Marco drives a red sedan."}]
Result: duplicate_candidate_id = 0 (synonym — "car" and "vehicle" refer to the same thing, same possessor)
</EXAMPLE>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static List<Message> NodeList(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are an entity deduplication assistant that groups duplicate nodes by UUID.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
Given the following context, deduplicate a list of nodes:

<NODES>
{{PromptHelpers.ToPromptJson(context["nodes"])}}
</NODES>

Task:
1. Group nodes together such that all duplicate nodes are in the same list of uuids.
2. All duplicate uuids should be grouped together in the same list.
3. Also return a new summary that synthesizes the summaries into a new short summary.

Guidelines:
1. Each uuid from the list of nodes should appear EXACTLY once in your response.
2. If a node has no duplicates, it should appear in the response in a list of only one uuid.

<EXAMPLE>
Input nodes:
[
  {"uuid": "a1", "name": "NYC", "summary": "New York City"},
  {"uuid": "b2", "name": "New York City", "summary": "The city of New York"},
  {"uuid": "c3", "name": "Los Angeles", "summary": "City in California"}
]

Result:
[
  {"uuids": ["a1", "b2"], "summary": "New York City, also known as NYC"},
  {"uuids": ["c3"], "summary": "City in California"}
]
</EXAMPLE>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["node"] = Node,
        ["node_list"] = NodeList,
        ["nodes"] = Nodes,
    };
}

internal abstract class DedupeNodesPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "node" => Node,
            "node_list" => NodeList,
            "nodes" => Nodes,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt Node(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt NodeList(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt Nodes(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultDedupeNodesPrompts : DedupeNodesPrompts
{
    internal override ChatPrompt Node(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(DedupeNodes.Node(context));

    internal override ChatPrompt NodeList(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(DedupeNodes.NodeList(context));

    internal override ChatPrompt Nodes(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(DedupeNodes.Nodes(context));
}
