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

using System.Text.Json;
using System.Text.RegularExpressions;
using Graphiti.Core.Prompts;
using Graphiti.PyCompat;

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

    // PORT: R13. Python: r'<{name}>\s*(.*?)\s*</{name}>' with re.S. The name enters the pattern as it is, with no
    // re.escape, so the pattern is built at each call. The static Regex.Match keeps a cache of the patterns.
    // PORT: no annotations. TYPE_HINTS.md gives Unknown for both parameters. Each caller passes text, and the result
    // is a regex group or None.
    internal static string? Tag(string text, string name)
    {
        var m = Regex.Match(
            text,
            "<" + name + ">" + PyRegex.Space + "*(.*?)" + PyRegex.Space + "*</" + name + ">",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[1].Value : null;
    }

    // PORT: R13. Python: r"\{.*?\}(?=\s*'?,|\s*'?\]$|\s*'?\]\s*$)" with re.S.
    private static readonly Regex ObjectSpanPattern = new(
        @"\{.*?\}(?=" + PyRegex.Space + @"*'?," + "|" + PyRegex.Space + @"*'?\]$|" + PyRegex.Space + @"*'?\]" + PyRegex.Space + @"*$)",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // PORT: the annotation `list[dict[str, Any]]` (R20). `json.loads` and `ast.literal_eval` can return a value of
    // another shape, which the result type cannot hold (the judge case parse_list.json_non_list returns a dict).
    // Py.AsList and Py.AsDict throw InvalidCastException for such a value, where Python returns it.
    internal static List<OrderedDictionary<string, object?>> ParseList(string? text)
    {
        if (text is null || PyText.Strip(text) == string.Empty)
        {
            return [];
        }

        try
        {
            return Py.AsList(PyJson.Loads(text)).Select(Py.AsDict).ToList();
        }
        catch (JsonException)
        {
            // PORT: Python passes
        }

        try
        {
            return Py.AsList(PyAst.LiteralEval(text)).Select(Py.AsDict).ToList();
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            // PORT: Python passes
        }

        List<OrderedDictionary<string, object?>> @out = [];
        foreach (Match m in ObjectSpanPattern.Matches(text))
        {
            var span = m.Value;
            object? obj = null;
            Func<string, object?>[] loaders = [PyJson.Loads, PyAst.LiteralEval];
            foreach (var loader in loaders)
            {
                try
                {
                    obj = loader(span);
                    break;
                }
                catch (Exception)
                {
                    // PORT: Python continues
                    continue;
                }
            }

            // PORT: a dict with keys that are not text (a PyDict) has no place in the result type, so Py.AsDict throws
            // InvalidCastException for it, where Python appends it.
            if (obj is not (OrderedDictionary<string, object?> or PyDict))
            {
                var raw = new OrderedDictionary<string, object?> { ["raw"] = span };
                foreach (var key in new[] { "candidate_id", "idx", "id" })
                {
                    // Python: rf'["\']{key}["\']\s*:\s*(-?\d+)'
                    var km = Regex.Match(
                        span,
                        "[\"']" + key + "[\"']" + PyRegex.Space + "*:" + PyRegex.Space + "*(-?" + PyRegex.Digit + "+)",
                        RegexOptions.CultureInvariant);
                    if (km.Success)
                    {
                        raw[key] = Py.Int(km.Groups[1].Value);
                    }
                }

                foreach (var key in new[] { "name", "fact" })
                {
                    // Python: rf'["\']{key}["\']\s*:\s*["\'](.*?)["\']\s*[,}}]' with re.S
                    var km = Regex.Match(
                        span,
                        "[\"']" + key + "[\"']" + PyRegex.Space + "*:" + PyRegex.Space + "*[\"'](.*?)[\"']" + PyRegex.Space + "*[,}]",
                        RegexOptions.Singleline | RegexOptions.CultureInvariant);
                    if (km.Success)
                    {
                        raw[key] = km.Groups[1].Value;
                    }
                }

                @out.Add(raw);
            }
            else
            {
                @out.Add(Py.AsDict(obj));
            }
        }

        return @out;
    }

    /// <summary>Drop PREVIOUS MESSAGES first, then halve the CURRENT MESSAGE from the front.</summary>
    // PORT: no annotations. The state is text, and the result is text or None.
    internal static string? ShrinkState(string state)
    {
        var prev = Tag(state, "PREVIOUS MESSAGES");
        if (!string.IsNullOrEmpty(prev) && PyText.Strip(prev) is not ("[]" or ""))
        {
            return PyText.Replace(state, prev, "[]", 1);
        }

        var cur = Tag(state, "CURRENT MESSAGE");
        if (!string.IsNullOrEmpty(cur) && Py.Len(cur) > 2000)
        {
            return PyText.Replace(state, cur, "..." + Py.Slice(cur, Py.FloorDiv(Py.Len(cur), 2), null), 1);
        }

        return null;
    }

    /// <summary>Decompose a rendered dedupe_nodes.nodes user message into state + questions.</summary>
    // PORT: no annotations. TYPE_HINTS.md gives a 4-tuple of the state text, the questions, and two lists from
    // parse_list. A tuple result is a value tuple (Part 3).
    internal static (string State, OrderedDictionary<string, OrderedDictionary<string, object?>> Questions, List<OrderedDictionary<string, object?>> Entities, List<OrderedDictionary<string, object?>> Existing) NodeN6(string user)
    {
        var entities = ParseList(Tag(user, "ENTITIES"));
        var existing = ParseList(Tag(user, "EXISTING ENTITIES"));
        var state = PyText.Split(user, "</EXISTING ENTITIES>")[0] + "</EXISTING ENTITIES>";
        var criteria = new OrderedDictionary<string, object?>();
        foreach (var c in existing)
        {
            if (c.TryGetValue("candidate_id", out var candidateId))
            {
                criteria[Py.Str(candidateId)] = "candidate_id " + Py.Str(candidateId) + ": " + Py.Str(Py.Get(c, "name"));
            }
        }

        criteria["none"] = NodeNone;
        var questions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
        foreach (var e in entities)
        {
            var entityType = Py.Get(e, "entity_type");
            questions["e" + Py.Str(e["id"])] = new OrderedDictionary<string, object?>
            {
                ["type"] = "choice",
                ["instructions"] =
                    "The entity with id " + Py.Str(e["id"]) + " in ENTITIES is \"" + Py.Str(Py.Get(e, "name")) + "\" "
                    + "(" + string.Join(", ", (Py.Truthy(entityType) ? Py.Iter(entityType) : []).Select(Py.AsString)) + "). It was extracted from the CURRENT MESSAGE. "
                    + "Which EXISTING ENTITY (by candidate_id) refers to the same real-world object or concept "
                    + "as this entity? Obey the RULES. Choose none if there is no duplicate.",
                ["criteria"] = criteria,
            };
        }

        return (state + RulesHeader + NodeRules, questions, entities, existing);
    }

    // PORT: no annotations. `f` is an item of parse_list. Internal, because judge cases call it (R2).
    internal static OrderedDictionary<string, object?> ContraStrict(string newFact, OrderedDictionary<string, object?> f)
    {
        return new OrderedDictionary<string, object?>
        {
            ["type"] = "noul",
            ["instructions"] =
                "Does the NEW FACT \"" + newFact + "\" directly contradict the fact idx=" + Py.Str(f["idx"]) + " "
                + "\"" + Py.Str(Py.Get(f, "fact", Py.Get(f, "raw", ""))) + "\", so that both cannot be true at the same time? A contradiction "
                + "needs explicit evidence that the old fact stopped being true or was replaced "
                + "(termination, replacement, transfer, or mutually exclusive current-state "
                + "language). New activity, progress, or status about the same entities does not "
                + "contradict a role, ownership, or membership fact.",
            ["criteria"] = new OrderedDictionary<string, object?>
            {
                ["true"] = "The old fact and the NEW FACT cannot both be true.",
                ["false"] = "Both facts can be true, or the NEW FACT is a different event or a duplicate.",
            },
        };
    }

    /// <summary>Decompose a rendered dedupe_edges.resolve_edge user message (strict contradiction).</summary>
    // PORT: no annotations. The same 4-tuple form as node_n6.
    internal static (string State, OrderedDictionary<string, OrderedDictionary<string, object?>> Questions, List<OrderedDictionary<string, object?>> Existing, List<OrderedDictionary<string, object?>> Candidates) EdgeE2(string user)
    {
        var existing = ParseList(Tag(user, "EXISTING FACTS"));
        var candidates = ParseList(Tag(user, "FACT INVALIDATION CANDIDATES"));
        var newFact = PyText.Strip(Tag(user, "NEW FACT") ?? string.Empty);
        List<string> lines = ["NEW FACT: " + newFact, string.Empty, "EXISTING FACTS:"];
        var existingLines = existing.Select(f => "  idx=" + Py.Str(Py.Get(f, "idx")) + ": " + Py.Str(Py.Get(f, "fact", Py.Get(f, "raw", "")))).ToList();
        lines.AddRange(existingLines.Count > 0 ? existingLines : ["  (none)"]);
        lines.AddRange(["", "FACT INVALIDATION CANDIDATES:"]);
        var candidateLines = candidates.Select(f => "  idx=" + Py.Str(Py.Get(f, "idx")) + ": " + Py.Str(Py.Get(f, "fact", Py.Get(f, "raw", "")))).ToList();
        lines.AddRange(candidateLines.Count > 0 ? candidateLines : ["  (none)"]);
        var state = string.Join("\n", lines);
        var questions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
        foreach (var f in existing)
        {
            questions["dup" + Py.Str(f["idx"])] = new OrderedDictionary<string, object?>
            {
                ["type"] = "noul",
                ["instructions"] =
                    "Does the existing fact idx=" + Py.Str(f["idx"]) + " \"" + Py.Str(Py.Get(f, "fact", Py.Get(f, "raw", ""))) + "\" "
                    + "state the same information as the NEW FACT \"" + newFact + "\", or already state "
                    + "all of the information in the NEW FACT? The wording or the language can be "
                    + "different, and the existing fact can have more detail. It is not a duplicate "
                    + "when the NEW FACT has a different numeric value, date, identifier, or "
                    + "qualifier, or information that the existing fact does not state.",
                ["criteria"] = new OrderedDictionary<string, object?>
                {
                    ["true"] =
                        "The existing fact states the same information as the NEW FACT, or all of "
                        + "it, about the same entities.",
                    ["false"] =
                        "The NEW FACT has information that the existing fact does not state, a "
                        + "detail is different, or the facts describe different events or "
                        + "relationships.",
                },
            };
        }

        foreach (var f in existing.Concat(candidates))
        {
            questions["con" + Py.Str(f["idx"])] = ContraStrict(newFact, f);
        }

        return (
            state + RulesHeader + "DUPLICATE: " + DupRules + "\nCONTRADICTION: " + ConRules,
            questions,
            existing,
            candidates);
    }

    // PORT: internal, because openai_decisions_client.py imports it and judge cases call it (R2).
    internal static string UserText(List<Message> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == "user")
            {
                return message.Content;
            }
        }

        throw new ArgumentException("rendered prompt has no user message");
    }

    // PORT: internal, because judge cases call it (R2).
    internal static string EncodePayload(string state, OrderedDictionary<string, OrderedDictionary<string, object?>> questions)
    {
        return PyJson.Dumps(
            new OrderedDictionary<string, object?> { ["instructions"] = string.Empty, ["state"] = state, ["questions"] = questions },
            ensureAscii: false);
    }

    /// <summary>Render the default prompt and convert it to n6 questions.</summary>
    // PORT: no annotations. The caller passes a builder from get_prompt_builder, and the result is the nested
    // `_builder` (TYPE_HINTS.md). Internal, because the evaluation tests call it with a builder of their own (R2).
    internal static ChatPromptFunction JevNodeBuilder(ChatPromptFunction defaultBuilder)
    {
        ChatPrompt Builder(OrderedDictionary<string, object?> context)
        {
            var messages = defaultBuilder(context).AsMessages();
            var (state, questions, _, _) = NodeN6(UserText(messages));
            return new ChatPrompt
            {
                System = new SystemMessage
                {
                    Content = messages[0].Role == "system" ? messages[0].Content : string.Empty,
                }.Validate(),
                User = new UserMessage { Content = EncodePayload(state, questions) }.Validate(),
            }.Validate();
        }

        return Builder;
    }

    /// <summary>Render the default prompt and convert it to e2 questions.</summary>
    // PORT: internal, as JevNodeBuilder is.
    internal static ChatPromptFunction JevEdgeBuilder(ChatPromptFunction defaultBuilder)
    {
        ChatPrompt Builder(OrderedDictionary<string, object?> context)
        {
            var messages = defaultBuilder(context).AsMessages();
            var (state, questions, _, _) = EdgeE2(UserText(messages));
            return new ChatPrompt
            {
                System = new SystemMessage
                {
                    Content = messages[0].Role == "system" ? messages[0].Content : string.Empty,
                }.Validate(),
                User = new UserMessage { Content = EncodePayload(state, questions) }.Validate(),
            }.Validate();
        }

        return Builder;
    }

    /// <summary>Map Jev choice answers onto NodeResolutions.</summary>
    internal static OrderedDictionary<string, object?> AssembleNodeAnswers(
        OrderedDictionary<string, object?> answers,
        List<OrderedDictionary<string, object?>> entities)
    {
        List<object?> resolutions = [];
        foreach (var entity in entities)
        {
            var answerValue = Py.Get(answers, "e" + Py.Str(entity["id"]));
            var answer = Py.AsDict(Py.Truthy(answerValue) ? answerValue : new OrderedDictionary<string, object?>());
            var probabilitiesValue = Py.Get(answer, "probabilities");
            var probabilities = Py.AsDict(Py.Truthy(probabilitiesValue) ? probabilitiesValue : new OrderedDictionary<string, object?>());
            object? choice;
            if (probabilities.Count > 0)
            {
                // max(probabilities.items(), key=lambda item: item[1])[0]: the first of the largest values (R14).
                KeyValuePair<string, object?>? best = null;
                foreach (var item in probabilities)
                {
                    if (best is null || Py.LessThan(best.Value.Value, item.Value))
                    {
                        best = item;
                    }
                }

                choice = best?.Key;
            }
            else
            {
                choice = Py.Get(answer, "choice");
            }

            // PORT: int(choice) of a value from open data has no PyCompat helper for an object (reported).
            var candidateId = choice is null or "none"
                ? -1
                : choice switch
                {
                    string s => Py.Int(s),
                    long l => l,
                    double d => Py.Int(d),
                    bool b => b ? 1L : 0L,
                    _ => throw new InvalidCastException("int() argument must be a string, a bytes-like object or a real number"),
                };
            resolutions.Add(new OrderedDictionary<string, object?>
            {
                ["id"] = entity["id"],
                ["name"] = Py.Get(entity, "name"),
                ["duplicate_candidate_id"] = candidateId,
            });
        }

        return new OrderedDictionary<string, object?> { ["entity_resolutions"] = resolutions };
    }

    /// <summary>Map Jev noul answers onto EdgeDuplicate.</summary>
    internal static OrderedDictionary<string, object?> AssembleEdgeAnswers(
        OrderedDictionary<string, object?> answers,
        List<long> existingIndices,
        List<long> contradictionIndices,
        double duplicateThreshold,
        double contradictionThreshold)
    {
        List<object?> duplicateFacts = [];
        foreach (var idx in existingIndices)
        {
            var answer = Py.Get(answers, "dup" + Py.Str(idx));
            var noul = Py.Get(Py.AsDict(Py.Truthy(answer) ? answer : new OrderedDictionary<string, object?>()), "noul", 0.0);
            if (Py.AsDouble(noul) >= duplicateThreshold)
            {
                duplicateFacts.Add(idx);
            }
        }

        List<object?> contradictedFacts = [];
        foreach (var idx in contradictionIndices)
        {
            var answer = Py.Get(answers, "con" + Py.Str(idx));
            var noul = Py.Get(Py.AsDict(Py.Truthy(answer) ? answer : new OrderedDictionary<string, object?>()), "noul", 0.0);
            if (Py.AsDouble(noul) >= contradictionThreshold)
            {
                contradictedFacts.Add(idx);
            }
        }

        return new OrderedDictionary<string, object?>
        {
            ["duplicate_facts"] = duplicateFacts,
            ["contradicted_facts"] = contradictedFacts,
        };
    }

    /// <summary>Return prompt overrides for the Jev n6 and e2 dedupe questions.</summary>
    // PORT: `library` is a ChatPromptLibrary, which is an IPromptLibrary for get_prompt_builder.
    internal static LLMPromptOverrides JevPromptOverrides(IPromptLibrary? library = null)
    {
        IPromptLibrary promptLibrary = library ?? Lib.DefaultChatPromptLibrary;
        return new LLMPromptOverrides
        {
            DedupeNodes = new LLMPromptOverrides.DedupeNodesGroup
            {
                Nodes = JevNodeBuilder(Lib.GetPromptBuilder(promptLibrary, "dedupe_nodes.nodes")),
            },
            DedupeEdges = new LLMPromptOverrides.DedupeEdgesGroup
            {
                ResolveEdge = JevEdgeBuilder(Lib.GetPromptBuilder(promptLibrary, "dedupe_edges.resolve_edge")),
            },
        };
    }
}
