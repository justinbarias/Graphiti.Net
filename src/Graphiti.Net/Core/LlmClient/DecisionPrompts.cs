// Ported from graphiti_core/llm_client/decision_prompts.py
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

using Graphiti.Core.Prompts;

internal static class DecisionPrompts
{
    internal const string RulesHeader = "\n\nRULES FOR THE QUESTIONS:\n";

    internal const string NodeRules =
        "Entities are duplicates only if they refer to the same real-world object or concept. "
        + "They are NOT duplicates if they are related but distinct, or if they have similar names or "
        + "purposes but refer to separate instances or concepts. The same name for different real-world "
        + "things (for example Java the programming language and Java the island) is not a duplicate. "
        + "An abbreviation, a synonym, or a more complete name for the same thing with the same "
        + "possessor is a duplicate (for example NYC and New York City; Marco's car and Marco's vehicle).";

    internal const string NodeNone =
        "No EXISTING ENTITY refers to the same real-world object or concept as this entity. "
        + "Related but distinct entities, separate instances, and entities that only share a name or "
        + "purpose are not duplicates.";

    internal const string DupRules =
        "A duplicate states identical factual information: the same assertion about the same "
        + "entities, also when the wording or the language is different. An existing fact is also a "
        + "duplicate when it already states all of the information in the NEW FACT, even if the "
        + "existing fact has more detail (for example, extra identifiers). It is NOT a duplicate when "
        + "the NEW FACT has a different numeric value, date, identifier, or key qualifier, when the "
        + "NEW FACT adds information that the existing fact does not state, or when the facts "
        + "describe different events.";

    internal const string ConRules =
        "A contradiction means that the NEW FACT and the old fact cannot both be true. It needs "
        + "explicit evidence that the old fact stopped being true or was replaced, such as termination, "
        + "replacement, transfer, or mutually exclusive current-state language (for example a changed "
        + "job title for the same job). These are NOT contradictions: a restatement, paraphrase, or "
        + "translation of the same fact; a repeated event; different events on different dates (Bob ran "
        + "5 miles on Tuesday and Bob ran 3 miles on Wednesday); recency alone; new activity, progress, "
        + "status, updates, blockers, or collaboration about the same person or project, which do not "
        + "contradict a role, ownership, responsibility, membership, or assignment fact; and a second "
        + "employer, role, or location that can be true at the same time.";

    // PORT: no annotations. TYPE_HINTS.md gives Unknown for both parameters. Each caller passes text, and the result
    // is a regex group or None.
    // TODO(port): tag
    internal static string? Tag(string text, string name)
    {
        throw new NotSupportedException("TODO(port): tag");
    }

    // PORT: the annotation `list[dict[str, Any]]` (R20). `json.loads` and `ast.literal_eval` can return a value of
    // another shape, which the result type cannot hold (the judge case parse_list.json_non_list returns a dict).
    // TODO(port): parse_list
    internal static List<OrderedDictionary<string, object?>> ParseList(string? text)
    {
        throw new NotSupportedException("TODO(port): parse_list");
    }

    // PORT: no annotations. The state is text, and the result is text or None.
    // TODO(port): shrink_state
    internal static string? ShrinkState(string state)
    {
        throw new NotSupportedException("TODO(port): shrink_state");
    }

    // PORT: no annotations. TYPE_HINTS.md gives a 4-tuple of the state text, the questions, and two lists from
    // parse_list. A tuple result is a value tuple (Part 3).
    // TODO(port): node_n6
    internal static (string State, OrderedDictionary<string, OrderedDictionary<string, object?>> Questions, List<OrderedDictionary<string, object?>> Entities, List<OrderedDictionary<string, object?>> Existing) NodeN6(string user)
    {
        throw new NotSupportedException("TODO(port): node_n6");
    }

    // PORT: no annotations. `f` is an item of parse_list. Internal, because judge cases call it (R2).
    // TODO(port): _contra_strict
    internal static OrderedDictionary<string, object?> ContraStrict(string newFact, OrderedDictionary<string, object?> f)
    {
        throw new NotSupportedException("TODO(port): _contra_strict");
    }

    // PORT: no annotations. The same 4-tuple form as node_n6.
    // TODO(port): edge_e2
    internal static (string State, OrderedDictionary<string, OrderedDictionary<string, object?>> Questions, List<OrderedDictionary<string, object?>> Existing, List<OrderedDictionary<string, object?>> Candidates) EdgeE2(string user)
    {
        throw new NotSupportedException("TODO(port): edge_e2");
    }

    // PORT: internal, because openai_decisions_client.py imports it and judge cases call it (R2).
    // TODO(port): _user_text
    internal static string UserText(List<Message> messages)
    {
        throw new NotSupportedException("TODO(port): _user_text");
    }

    // PORT: internal, because judge cases call it (R2).
    // TODO(port): _encode_payload
    internal static string EncodePayload(string state, OrderedDictionary<string, OrderedDictionary<string, object?>> questions)
    {
        throw new NotSupportedException("TODO(port): _encode_payload");
    }

    // PORT: no annotations. The caller passes a builder from get_prompt_builder, and the result is the nested
    // `_builder` (TYPE_HINTS.md).
    // TODO(port): _jev_node_builder
    private static ChatPromptFunction JevNodeBuilder(ChatPromptFunction defaultBuilder)
    {
        throw new NotSupportedException("TODO(port): _jev_node_builder");
    }

    // TODO(port): _jev_edge_builder
    private static ChatPromptFunction JevEdgeBuilder(ChatPromptFunction defaultBuilder)
    {
        throw new NotSupportedException("TODO(port): _jev_edge_builder");
    }

    // TODO(port): assemble_node_answers
    internal static OrderedDictionary<string, object?> AssembleNodeAnswers(
        OrderedDictionary<string, object?> answers,
        List<OrderedDictionary<string, object?>> entities)
    {
        throw new NotSupportedException("TODO(port): assemble_node_answers");
    }

    // TODO(port): assemble_edge_answers
    internal static OrderedDictionary<string, object?> AssembleEdgeAnswers(
        OrderedDictionary<string, object?> answers,
        List<long> existingIndices,
        List<long> contradictionIndices,
        double duplicateThreshold,
        double contradictionThreshold)
    {
        throw new NotSupportedException("TODO(port): assemble_edge_answers");
    }

    // PORT: `library` is a ChatPromptLibrary, which is an IPromptLibrary for get_prompt_builder.
    // TODO(port): jev_prompt_overrides
    internal static LLMPromptOverrides JevPromptOverrides(IPromptLibrary? library = null)
    {
        throw new NotSupportedException("TODO(port): jev_prompt_overrides");
    }
}
