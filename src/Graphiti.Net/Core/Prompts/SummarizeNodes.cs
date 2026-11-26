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

using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

internal sealed record Summary : IPydanticModel
{
    // PORT: the suffix Value (R2). C# does not allow a member named like its type (error CS0542), so `summary` is SummaryValue. The wire name stays `summary`.
    internal required string SummaryValue { get; set; }

    internal Summary Validate() => this;

    internal static Summary ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("Summary", data);
        var summary = f.Required("summary", Pydantic.Str);
        f.ThrowIfAny();
        return new Summary { SummaryValue = summary.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("summary", SummaryValue));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("Summary")
        .Required("summary", PydanticType.Str, "Summary containing the important information about the entity. " + "Under " + Py.Str(TextUtils.MaxSummaryChars) + " characters");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("Summary", ModelJsonSchema);
}

internal sealed record SummaryDescription : IPydanticModel
{
    internal required string Description { get; set; }

    internal SummaryDescription Validate() => this;

    internal static SummaryDescription ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SummaryDescription", data);
        var description = f.Required("description", Pydantic.Str);
        f.ThrowIfAny();
        return new SummaryDescription { Description = description.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("description", Description));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SummaryDescription")
        .Required("description", PydanticType.Str, "One sentence description of the provided summary");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("SummaryDescription", ModelJsonSchema);
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
    internal static List<Message> SummarizePair(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are a helpful assistant that combines summaries into a single dense factual summary.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
        Synthesize the information from the following two summaries into a single information-dense summary.

        IMPORTANT:
        - Preserve all materially relevant names, roles, places, dates, counts, and changes over time that are explicitly supported.
        - Prefer compact factual sentences over vague thematic phrasing.
        - When the durable fact is the content of what was said, state the content directly instead of narrating that it was said.
        - Use communication verbs only when the act of speaking, asking, sharing, presenting, or announcing is itself the important fact.
        - Avoid filler verbs like "mentioned", "described", "stated", "reported", "noted", "discussed", "referenced", and "indicated" unless the communication act itself matters.
        - SUMMARIES MUST BE LESS THAN {{Py.Str(TextUtils.MaxSummaryChars)}} CHARACTERS.

        Summaries:
        {{PromptHelpers.ToPromptJson(context["node_summaries"])}}
""" + "\n        ",
            }.Validate(),
        ];
    }

    internal static List<Message> SummarizeContext(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are a helpful assistant that generates detailed, information-dense summaries and attributes from provided text.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
        Given the MESSAGES and the ENTITY name, create a summary for the ENTITY. Your summary must only use
        information from the provided MESSAGES. Your summary should also only contain information relevant to the
        provided ENTITY.

        In addition, extract any values for the provided entity properties based on their descriptions.
        If the value of the entity property cannot be found in the current context, set the value of the property to the Python value None.

        {{Snippets.SummaryInstructions}}

        <MESSAGES>
        {{PromptHelpers.ToPromptJson(context["previous_episodes"])}}
        {{PromptHelpers.ToPromptJson(context["episode_content"])}}
        </MESSAGES>

        <ENTITY>
        {{Py.Str(context["node_name"])}}
        </ENTITY>

        <ENTITY CONTEXT>
        {{Py.Str(context["node_summary"])}}
        </ENTITY CONTEXT>

        <ATTRIBUTES>
        {{PromptHelpers.ToPromptJson(context["attributes"])}}
        </ATTRIBUTES>
""" + "\n        ",
            }.Validate(),
        ];
    }

    internal static List<Message> SummaryDescription(OrderedDictionary<string, object?> context)
    {
        return
        [
            new Message
            {
                Role = "system",
                Content = "You are a helpful assistant that describes provided contents in a single sentence.",
            }.Validate(),
            new Message
            {
                Role = "user",
                Content = "\n" + $$"""
        Create a short one sentence description of the summary that explains what kind of information is summarized.
        Summaries must be under {{Py.Str(TextUtils.MaxSummaryChars)}} characters.

        Summary:
        {{PromptHelpers.ToPromptJson(context["summary"])}}
""" + "\n        ",
            }.Validate(),
        ];
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
    internal override ChatPrompt SummarizePair(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(SummarizeNodes.SummarizePair(context));

    internal override ChatPrompt SummarizeContext(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(SummarizeNodes.SummarizeContext(context));

    internal override ChatPrompt SummaryDescription(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(SummarizeNodes.SummaryDescription(context));
}
