// Ported from graphiti_core/prompts/summarize_sagas.py
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

internal sealed record SagaSummary : IPydanticModel
{
    internal required string Summary { get; set; }

    internal SagaSummary Validate() => this;

    internal static SagaSummary ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SagaSummary", data);
        var summary = f.Required("summary", Pydantic.Str);
        f.ThrowIfAny();
        return new SagaSummary { Summary = summary.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("summary", Summary));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SagaSummary")
        .Required("summary", PydanticType.Str, "Factual knowledge brief. Under " + Py.Str(TextUtils.MaxSummaryChars) + " characters.");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("SagaSummary", ModelJsonSchema);
}

internal interface SummarizeSagasPrompt
{
    PromptFunction SummarizeSaga { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class SummarizeSagas
{
    internal static List<Message> SummarizeSaga(OrderedDictionary<string, object?> context)
    {
        var sagaName = Py.Get<string, object?>(context, "saga_name", "Unknown");
        var existingSummary = Py.Get<string, object?>(context, "existing_summary", "");
        var episodes = Py.Get<string, object?>(context, "episodes", new List<object?>());

        var episodesText = Py.Truthy(episodes) ? string.Join("\n---\n", Py.Iter(episodes).Select(Py.AsString)) : "(no messages)";

        var existingSummarySection = "";
        if (Py.Truthy(existingSummary))
        {
            // PORT: R9. The Python literal starts and ends with a line break, and a backslash joins two lines.
            existingSummarySection = "\n" + $$"""
<EXISTING_KNOWLEDGE>
{{Py.Str(existingSummary)}}
</EXISTING_KNOWLEDGE>
The EXISTING_KNOWLEDGE contains previously extracted facts. Merge any new facts from MESSAGES into it. When newer messages contradict older facts, prefer the newer fact. If MESSAGES add no new durable facts, return the existing knowledge unchanged.
""" + "\n";
        }

        return
        [
            new Message
            {
                Role = "system",
                Content = "You extract durable knowledge from message threads. "
                    + "Output a factual knowledge brief — facts, decisions, preferences, plans, "
                    + "entities, and relationships — that stands alone without reference to the "
                    + "original messages. "
                    + "Stay under " + Py.Str(TextUtils.MaxSummaryChars) + " characters.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = $$"""
NEVER use meta-language verbs: "mentioned", "discussed", "noted", "stated", "described", "referenced", "indicated", "reported", "talked about", "brought up" — these describe conversational dynamics, not knowledge. State facts directly instead.
NEVER refer to the messages, conversation, thread, or participants' communicative acts. The output must read as if no conversation happened — only the facts matter.
NEVER begin with "This conversation", "The thread", "In this thread", or "The discussion".
NEVER infer preferences or habits from a single passing mention. When a person explicitly states a preference ("I prefer X", "I love X", "I always do X"), capture it as a stated preference attributed to that person.

Your task: extract all durable knowledge from the MESSAGES below and produce a factual knowledge brief for the topic "{{Py.Str(sagaName)}}".

Capture explicitly stated:
- Facts and concrete details (names, dates, numbers, locations)
- Decisions and their outcomes
- Preferences and requirements (when a person explicitly claims them)
- Plans, next steps, and commitments
- Relationships between entities (who works where, who owns what)
- State changes (what was X, now is Y)

Write 2-6 dense sentences. Use third person. Preserve all names, dates, counts, and temporal qualifiers. Lead with the most important fact or decision.
{{existingSummarySection}}
<MESSAGES>
{{episodesText}}
</MESSAGES>

<EXAMPLES>
MESSAGES: "Jordan: We decided to move the deployment to March 15 instead of March 8. The staging environment isn't ready.\n---\nPriya: Agreed. I'll update the client timeline. We also need to switch from PostgreSQL to CockroachDB for the multi-region requirement."
GOOD: "Deployment moved from March 8 to March 15 because the staging environment is not ready. Priya owns updating the client timeline. The database is switching from PostgreSQL to CockroachDB to support the multi-region requirement."
BAD: "Jordan mentioned moving the deployment date. Priya discussed updating the timeline and talked about switching databases. The team noted staging issues."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Alex: I tried the new Thai place on Elm Street last night — the pad see ew was incredible. Definitely going back.\n---\nMina: Oh nice, I've been wanting to try that. Is it the one next to the bookstore?\n---\nAlex: Yeah, Siam Kitchen. They're open until 11 PM on weekends."
GOOD: "Siam Kitchen is a Thai restaurant on Elm Street, next to a bookstore, open until 11 PM on weekends. Alex considers the pad see ew excellent."
BAD: "Alex mentioned trying a new Thai place and discussed the pad see ew. Mina asked about the location. Alex noted it was Siam Kitchen and stated the weekend hours."
</EXAMPLES>

<EXAMPLES>
MESSAGES: "Sam: I really prefer working in the mornings — I'm way more productive before noon.\n---\nDana: Same. I've been blocking 9-11 AM for deep work. Also, I can't stand Jira — can we move the tracker to Linear?\n---\nSam: Fine by me. I'll set up the workspace."
GOOD: "Sam prefers morning work and reports higher productivity before noon. Dana blocks 9-11 AM for deep work. Dana prefers Linear over Jira for issue tracking. Sam is setting up the Linear workspace."
BAD: "Sam and Dana discussed their work preferences. They talked about morning productivity and mentioned switching from Jira to Linear."
</EXAMPLES>
""" + "\n",
            }.Validate(),
        ];
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["summarize_saga"] = SummarizeSaga,
    };
}

internal abstract class SummarizeSagasPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "summarize_saga" => SummarizeSaga,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt SummarizeSaga(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultSummarizeSagasPrompts : SummarizeSagasPrompts
{
    internal override ChatPrompt SummarizeSaga(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(SummarizeSagas.SummarizeSaga(context));
}
