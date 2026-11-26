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
using Graphiti.PyCompat;

// PORT: the module name equals the class name LLMRuntime, so the module class has the suffix Module (R2).
internal static class LlmRuntimeModule
{
    // PORT: internal, because the class LLMRuntime calls it (R2).
    internal static ChatPromptFunction WrapBuilder(ChatPromptFunction builder, string promptName)
    {
        return context => Lib.EnsureChatPrompt(builder(context), promptName);
    }
}

/// <summary>
/// Route prompts across one or more provider transports.
/// The runtime uses the routed model ID for each prompt. An unrouted prompt uses the default model.
/// <c>model_size</c> does not select a second model on this path. Route prompts that need a smaller model to that
/// model explicitly.
/// </summary>
internal sealed class LLMRuntime
{
    // PORT: `routes`, `prompt_overrides`, and `library` are keyword-only in Python.
    internal LLMRuntime(
        LLMModel model,
        PromptRoutes? routes = null,
        LLMPromptOverrides? promptOverrides = null,
        IPromptLibrary? library = null)
    {
        // PORT: `isinstance(model, LLMModel)`, `isinstance(routes, PromptRoutes)`, and `isinstance(prompt_overrides,
        // LLMPromptOverrides)` are true for each typed parameter, so the three TypeErrors of Python have no C# form.
        var resolvedRoutes = PromptConfig.FlattenRoutes(routes);
        var resolvedOverrides = PromptConfig.FlattenOverrides(promptOverrides);

        IPromptLibrary resolvedLibrary;
        if (library is null)
        {
            resolvedLibrary = Lib.EnsurePromptLibraryWrapped(Lib.CreatePromptLibrary());
        }
        else
        {
            resolvedLibrary = Lib.EnsurePromptLibraryWrapped(library);
        }

        List<LLMTransport> transports = [];
        foreach (var routedModel in resolvedRoutes.Values.Prepend(model))
        {
            var transport = routedModel.Transport;
            if (!transports.Any(existing => ReferenceEquals(transport, existing)))
            {
                transports.Add(transport);
            }
        }

        Transports = transports;
        Model = model;
        Routes = resolvedRoutes;
        PromptOverrides = resolvedOverrides;
        Library = resolvedLibrary;
    }

    // PORT: `tuple[LLMTransport[Any], ...]` (Part 3).
    internal IReadOnlyList<LLMTransport> Transports { get; set; }

    internal LLMModel Model { get; set; }

    internal OrderedDictionary<string, LLMModel> Routes { get; set; }

    internal OrderedDictionary<string, ChatPromptFunction> PromptOverrides { get; set; }

    // PORT: the result of ensure_prompt_library_wrapped (`Any`), an IPromptLibrary (R15).
    internal IPromptLibrary Library { get; set; }

    internal LLMClient Client => Model.Transport.Client;

    internal void SetTracer(Tracer tracer)
    {
        HashSet<LLMClient> seenClients = new(ReferenceEqualityComparer.Instance);
        foreach (var transport in Transports)
        {
            var client = transport.Client;
            if (seenClients.Contains(client))
            {
                continue;
            }

            seenClients.Add(client);
            client.SetTracer(tracer);
        }
    }

    /// <summary>Return the LLMModel that should run <paramref name="promptName"/>.</summary>
    internal LLMModel ResolveModel(string promptName)
    {
        if (Routes.TryGetValue(promptName, out var routed))
        {
            return routed;
        }

        var groupName = PyText.Split(promptName, ".", 1)[0];
        if (Routes.TryGetValue(groupName, out routed))
        {
            return routed;
        }

        return Model;
    }

    /// <summary>Builder resolution: model override → general override → library method.</summary>
    internal ChatPromptFunction ResolveBuilder(string promptName, LLMModel model)
    {
        if (!model.FlatOverrides.TryGetValue(promptName, out var @override))
        {
            PromptOverrides.TryGetValue(promptName, out @override);
        }

        if (@override is not null)
        {
            return LlmRuntimeModule.WrapBuilder(@override, promptName);
        }

        return Lib.GetPromptBuilder(Library, promptName);
    }

    // PORT: `prompt_name: PromptName` is a `Literal`, so `string` (R7). The arguments after `context` are
    // keyword-only in Python.
    internal async Task<OrderedDictionary<string, object?>> CompleteAsync(
        string promptName,
        OrderedDictionary<string, object?> context,
        IResponseModel? responseModel = null,
        bool attributeExtraction = false,
        string? groupId = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        CancellationToken cancellationToken = default)
    {
        var resolvedSchema = Lib.ResolveResponseModel(promptName, responseModel);
        var model = ResolveModel(promptName);
        var builder = ResolveBuilder(promptName, model);
        var messages = builder(context).AsMessages();
        var effectiveMaxTokens = maxTokens ?? model.MaxTokens;

        return await model.Transport.Client.GenerateResponseAsync(
            messages,
            responseModel: resolvedSchema,
            maxTokens: effectiveMaxTokens,
            modelSize: modelSize,
            groupId: groupId,
            promptName: promptName,
            attributeExtraction: attributeExtraction,
            model: model.Id,
            cancellationToken: cancellationToken);
    }
}
