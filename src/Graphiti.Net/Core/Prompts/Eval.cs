// Ported from graphiti_core/prompts/eval.py
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

internal sealed record QueryExpansion : IPydanticModel
{
    internal required string Query { get; set; }

    internal QueryExpansion Validate() => this;

    internal static QueryExpansion ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("QueryExpansion", data);
        var query = f.Required("query", Pydantic.Str);
        f.ThrowIfAny();
        return new QueryExpansion { Query = query.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("query", Query));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("QueryExpansion")
        .Required("query", PydanticType.Str, "query optimized for database search");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("QueryExpansion", ModelJsonSchema);
}

internal sealed record QAResponse : IPydanticModel
{
    internal required string Answer { get; set; }

    internal QAResponse Validate() => this;

    internal static QAResponse ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("QAResponse", data);
        var answer = f.Required("ANSWER", Pydantic.Str);
        f.ThrowIfAny();
        return new QAResponse { Answer = answer.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("ANSWER", Answer));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("QAResponse")
        .Required("ANSWER", PydanticType.Str, "how Alice would answer the question");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("QAResponse", ModelJsonSchema);
}

internal sealed record EvalResponse : IPydanticModel
{
    internal required bool IsCorrect { get; set; }

    internal required string Reasoning { get; set; }

    internal EvalResponse Validate() => this;

    internal static EvalResponse ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EvalResponse", data);
        var isCorrect = f.Required("is_correct", Pydantic.Bool);
        var reasoning = f.Required("reasoning", Pydantic.Str);
        f.ThrowIfAny();
        return new EvalResponse { IsCorrect = isCorrect.Value, Reasoning = reasoning.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("is_correct", IsCorrect), ("reasoning", Reasoning));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EvalResponse")
        .Required("is_correct", PydanticType.Bool, "boolean if the answer is correct or incorrect")
        .Required("reasoning", PydanticType.Str, "why you determined the response was correct or incorrect");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("EvalResponse", ModelJsonSchema);
}

internal sealed record EvalAddEpisodeResults : IPydanticModel
{
    internal required bool CandidateIsWorse { get; set; }

    internal required string Reasoning { get; set; }

    internal EvalAddEpisodeResults Validate() => this;

    internal static EvalAddEpisodeResults ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EvalAddEpisodeResults", data);
        var candidateIsWorse = f.Required("candidate_is_worse", Pydantic.Bool);
        var reasoning = f.Required("reasoning", Pydantic.Str);
        f.ThrowIfAny();
        return new EvalAddEpisodeResults { CandidateIsWorse = candidateIsWorse.Value, Reasoning = reasoning.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("candidate_is_worse", CandidateIsWorse), ("reasoning", Reasoning));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EvalAddEpisodeResults")
        .Required("candidate_is_worse", PydanticType.Bool, "boolean if the baseline extraction is higher quality than the candidate extraction.")
        .Required("reasoning", PydanticType.Str, "why you determined the response was correct or incorrect");

    internal static readonly IResponseModel ResponseModel = new BuiltinResponseModel("EvalAddEpisodeResults", ModelJsonSchema);
}

internal interface EvalPrompt
{
    PromptFunction QaPrompt { get; }

    // PORT: the suffix Function (R2). C# does not allow a member named like its
    // type (error CS0542), so `eval_prompt` is EvalPromptFunction here. The interface name is the one of R2.
    PromptFunction EvalPromptFunction { get; }

    PromptFunction QueryExpansion { get; }

    PromptFunction EvalAddEpisodeResults { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class Eval
{
    internal static List<Message> QueryExpansion(OrderedDictionary<string, object?> context)
    {
        var sysPrompt = "You are an expert at rephrasing questions into queries used in a database retrieval system";

        var userPrompt = "\n" + $$"""
    Bob is asking Alice a question, are you able to rephrase the question into a simpler one about Alice in the third person
    that maintains the relevant context?
    <QUESTION>
    {{PromptHelpers.ToPromptJson(context["query"])}}
    </QUESTION>
""" + "\n    ";
        return
        [
            new Message { Role = "system", Content = sysPrompt }.Validate(),
            new Message { Role = "user", Content = userPrompt }.Validate(),
        ];
    }

    internal static List<Message> QaPrompt(OrderedDictionary<string, object?> context)
    {
        var sysPrompt = "You are Alice and should respond to all questions from the first person perspective of Alice";

        var userPrompt = "\n" + $$"""
    Your task is to briefly answer the question in the way that you think Alice would answer the question.
    You are given the following entity summaries and facts to help you determine the answer to your question.
    <ENTITY_SUMMARIES>
    {{PromptHelpers.ToPromptJson(context["entity_summaries"])}}
    </ENTITY_SUMMARIES>
    <FACTS>
    {{PromptHelpers.ToPromptJson(context["facts"])}}
    </FACTS>
    <QUESTION>
    {{Py.Str(context["query"])}}
    </QUESTION>
""" + "\n    ";
        return
        [
            new Message { Role = "system", Content = sysPrompt }.Validate(),
            new Message { Role = "user", Content = userPrompt }.Validate(),
        ];
    }

    internal static List<Message> EvalPrompt(OrderedDictionary<string, object?> context)
    {
        var sysPrompt = "You are a judge that determines if answers to questions match a gold standard answer";

        // PORT: R9. The line "Although ... topic " ends in a space, so it is an ordinary string.
        var userPrompt = "\n" + """
    Given the QUESTION and the gold standard ANSWER determine if the RESPONSE to the question is correct or incorrect.
    Although the RESPONSE may be more verbose, mark it as correct as long as it references the same topic
""" + " \n" + $$"""
    as the gold standard ANSWER. Also include your reasoning for the grade.
    <QUESTION>
    {{Py.Str(context["query"])}}
    </QUESTION>
    <ANSWER>
    {{Py.Str(context["answer"])}}
    </ANSWER>
    <RESPONSE>
    {{Py.Str(context["response"])}}
    </RESPONSE>
""" + "\n    ";
        return
        [
            new Message { Role = "system", Content = sysPrompt }.Validate(),
            new Message { Role = "user", Content = userPrompt }.Validate(),
        ];
    }

    internal static List<Message> EvalAddEpisodeResults(OrderedDictionary<string, object?> context)
    {
        var sysPrompt = """
You are a judge that determines whether a baseline graph building result from a list of messages is better
        than a candidate graph building result based on the same messages.
""";

        // PORT: R9. Each line that ends in spaces, or holds only spaces, is an ordinary string.
        var userPrompt = "\n" + """
    Given the following PREVIOUS MESSAGES and MESSAGE, determine if the BASELINE graph data extracted from the
""" + " \n" + """
    conversation is higher quality than the CANDIDATE graph data extracted from the conversation.
""" + "\n    \n" + """
    Return False if the BASELINE extraction is better, and True otherwise. If the CANDIDATE extraction and
    BASELINE extraction are nearly identical in quality, return True. Add your reasoning for your decision to the reasoning field
""" + "\n    \n" + $$"""
    <PREVIOUS MESSAGES>
    {{Py.Str(context["previous_messages"])}}
    </PREVIOUS MESSAGES>
    <MESSAGE>
    {{Py.Str(context["message"])}}
    </MESSAGE>
""" + "\n    \n" + $$"""
    <BASELINE>
    {{Py.Str(context["baseline"])}}
    </BASELINE>
""" + "\n    \n" + $$"""
    <CANDIDATE>
    {{Py.Str(context["candidate"])}}
    </CANDIDATE>
""" + "\n    ";
        return
        [
            new Message { Role = "system", Content = sysPrompt }.Validate(),
            new Message { Role = "user", Content = userPrompt }.Validate(),
        ];
    }

    internal static readonly OrderedDictionary<string, PromptFunction> Versions = new()
    {
        ["qa_prompt"] = QaPrompt,
        ["eval_prompt"] = EvalPrompt,
        ["query_expansion"] = QueryExpansion,
        ["eval_add_episode_results"] = EvalAddEpisodeResults,
    };
}

internal abstract class EvalPrompts : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        function = name switch
        {
            "query_expansion" => QueryExpansion,
            "qa_prompt" => QaPrompt,
            "eval_prompt" => EvalPrompt,
            "eval_add_episode_results" => EvalAddEpisodeResults,
            _ => null,
        };
        return function is not null;
    }

    internal abstract ChatPrompt QueryExpansion(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt QaPrompt(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt EvalPrompt(OrderedDictionary<string, object?> context);

    internal abstract ChatPrompt EvalAddEpisodeResults(OrderedDictionary<string, object?> context);
}

internal sealed class DefaultEvalPrompts : EvalPrompts
{
    internal override ChatPrompt QueryExpansion(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(Eval.QueryExpansion(context));

    internal override ChatPrompt QaPrompt(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(Eval.QaPrompt(context));

    internal override ChatPrompt EvalPrompt(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(Eval.EvalPrompt(context));

    internal override ChatPrompt EvalAddEpisodeResults(OrderedDictionary<string, object?> context) =>
        ChatPrompt.FromMessages(Eval.EvalAddEpisodeResults(context));
}
