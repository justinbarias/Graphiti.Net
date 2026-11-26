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

// PORT: each response model has its fields as properties. The unit adds the members of R7.
internal sealed record QueryExpansion
{
    internal required string Query { get; set; }
}

internal sealed record QAResponse
{
    // PORT: the Python field name is `ANSWER`. Its wire name stays `ANSWER` (R2).
    internal required string Answer { get; set; }
}

internal sealed record EvalResponse
{
    internal required bool IsCorrect { get; set; }

    internal required string Reasoning { get; set; }
}

internal sealed record EvalAddEpisodeResults
{
    internal required bool CandidateIsWorse { get; set; }

    internal required string Reasoning { get; set; }
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
    // TODO(port): query_expansion
    internal static List<Message> QueryExpansion(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): query_expansion");
    }

    // TODO(port): qa_prompt
    internal static List<Message> QaPrompt(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): qa_prompt");
    }

    // TODO(port): eval_prompt
    internal static List<Message> EvalPrompt(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): eval_prompt");
    }

    // TODO(port): eval_add_episode_results
    internal static List<Message> EvalAddEpisodeResults(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): eval_add_episode_results");
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
    // TODO(port): DefaultEvalPrompts.query_expansion
    internal override ChatPrompt QueryExpansion(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultEvalPrompts.query_expansion");
    }

    // TODO(port): DefaultEvalPrompts.qa_prompt
    internal override ChatPrompt QaPrompt(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultEvalPrompts.qa_prompt");
    }

    // TODO(port): DefaultEvalPrompts.eval_prompt
    internal override ChatPrompt EvalPrompt(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultEvalPrompts.eval_prompt");
    }

    // TODO(port): DefaultEvalPrompts.eval_add_episode_results
    internal override ChatPrompt EvalAddEpisodeResults(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultEvalPrompts.eval_add_episode_results");
    }
}
