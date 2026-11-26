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

// PORT: a response model has its fields as properties. The unit adds the members of R7.
internal sealed record SagaSummary
{
    internal required string Summary { get; set; }
}

internal interface SummarizeSagasPrompt
{
    PromptFunction SummarizeSaga { get; }
}

// PORT: `Versions` is a TypedDict that prompts/lib.py reads by key, so it has no C# type (R2).
internal static class SummarizeSagas
{
    // TODO(port): summarize_saga
    internal static List<Message> SummarizeSaga(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): summarize_saga");
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
    // TODO(port): DefaultSummarizeSagasPrompts.summarize_saga
    internal override ChatPrompt SummarizeSaga(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): DefaultSummarizeSagasPrompts.summarize_saga");
    }
}
