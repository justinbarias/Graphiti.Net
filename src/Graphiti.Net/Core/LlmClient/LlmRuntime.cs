// Ported from graphiti_core/llm_client/llm_runtime.py
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
using Graphiti.Internal;

// PORT: the module name equals the class name LLMRuntime, so the module class has the suffix Module (R2).
internal static class LlmRuntimeModule
{
    // PORT: internal, because the class LLMRuntime calls it (R2).
    // TODO(port): _wrap_builder
    internal static ChatPromptFunction WrapBuilder(ChatPromptFunction builder, string promptName)
    {
        throw new NotSupportedException("TODO(port): _wrap_builder");
    }
}

internal sealed class LLMRuntime
{
    // PORT: `routes`, `prompt_overrides`, and `library` are keyword-only in Python.
    // TODO(port): LLMRuntime.__init__
    internal LLMRuntime(
        LLMModel model,
        PromptRoutes? routes = null,
        LLMPromptOverrides? promptOverrides = null,
        IPromptLibrary? library = null)
    {
        throw new NotSupportedException("TODO(port): LLMRuntime.__init__");
    }

    // PORT: `tuple[LLMTransport[Any], ...]` (Part 3).
    internal IReadOnlyList<LLMTransport> Transports { get; set; }

    internal LLMModel Model { get; set; }

    internal OrderedDictionary<string, LLMModel> Routes { get; set; }

    internal OrderedDictionary<string, ChatPromptFunction> PromptOverrides { get; set; }

    // PORT: the result of ensure_prompt_library_wrapped (`Any`), an IPromptLibrary (R15).
    internal IPromptLibrary Library { get; set; }

    // TODO(port): LLMRuntime.client
    internal LLMClient Client => throw new NotSupportedException("TODO(port): LLMRuntime.client");

    // TODO(port): LLMRuntime.set_tracer
    internal void SetTracer(Tracer tracer)
    {
        throw new NotSupportedException("TODO(port): LLMRuntime.set_tracer");
    }

    // TODO(port): LLMRuntime.resolve_model
    internal LLMModel ResolveModel(string promptName)
    {
        throw new NotSupportedException("TODO(port): LLMRuntime.resolve_model");
    }

    // TODO(port): LLMRuntime.resolve_builder
    internal ChatPromptFunction ResolveBuilder(string promptName, LLMModel model)
    {
        throw new NotSupportedException("TODO(port): LLMRuntime.resolve_builder");
    }

    // PORT: `prompt_name: PromptName` is a `Literal`, so `string` (R7). The arguments after `context` are
    // keyword-only in Python.
    // TODO(port): LLMRuntime.complete
    internal Task<OrderedDictionary<string, object?>> CompleteAsync(
        string promptName,
        OrderedDictionary<string, object?> context,
        IResponseModel? responseModel = null,
        bool attributeExtraction = false,
        string? groupId = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): LLMRuntime.complete");
    }
}
