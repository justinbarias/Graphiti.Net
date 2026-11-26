// Ported from graphiti_core/graphiti_types.py
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

namespace Graphiti.Core;

using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;

// PORT: a pydantic model with `arbitrary_types_allowed` (R7). Its fields are properties, and its unit adds the R7
// members that a caller needs. `getattr(clients, 'max_coroutines', None)` and `getattr(clients, 'tracer', None)`
// read the declared fields (R15).
internal sealed record GraphitiClients
{
    internal required GraphDriver Driver { get; set; }

    internal required LLMClient LlmClient { get; set; }

    internal required EmbedderClient Embedder { get; set; }

    internal required CrossEncoderClient CrossEncoder { get; set; }

    internal required Tracer Tracer { get; set; }

    // PORT: `prompt_library: Any = None`. Each value is a prompt library that callers read by name (R15): the
    // result of create_prompt_library or of ensure_prompt_library_wrapped.
    internal IPromptLibrary? PromptLibrary { get; set; }

    internal LLMRuntime? LlmRuntime { get; set; }

    internal long? MaxCoroutines { get; set; }

    // PORT: the arguments after `context` are keyword-only in Python. `prompt_name: PromptName` is `string` (R7).
    // TODO(port): GraphitiClients.complete_prompt
    internal Task<OrderedDictionary<string, object?>> CompletePromptAsync(
        string promptName,
        OrderedDictionary<string, object?> context,
        IResponseModel? responseModel = null,
        ModelSize modelSize = ModelSize.Medium,
        bool attributeExtraction = false,
        string? groupId = null,
        long? maxTokens = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): GraphitiClients.complete_prompt");
    }
}

// PORT: the module class of R2, split around the classes to keep the order of the Python file.
internal static partial class GraphitiTypes
{
    // TODO(port): uses_prompt_routing
    internal static bool UsesPromptRouting(GraphitiClients? clients)
    {
        throw new NotSupportedException("TODO(port): uses_prompt_routing");
    }

    // PORT: `clients` is keyword-only in Python.
    // TODO(port): resolve_prompt_messages
    internal static List<Message> ResolvePromptMessages(
        string promptName,
        PromptFunction legacyPrompt,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null)
    {
        throw new NotSupportedException("TODO(port): resolve_prompt_messages");
    }
}

// PORT: a TypedDict (R7) with `total=False`, so each key can be absent.
// PORT: a TypedDict with `total=False` has nullable fields, and null stands for an absent key (R7).
// generate_prompt_response builds it as the keyword arguments of generate_response.
internal sealed record GeneratePromptResponseKwargs
{
    internal IResponseModel? ResponseModel { get; set; }

    internal string? PromptName { get; set; }

    internal long? MaxTokens { get; set; }

    internal ModelSize? ModelSize { get; set; }

    internal string? GroupId { get; set; }

    internal bool? AttributeExtraction { get; set; }
}

internal static partial class GraphitiTypes
{
    // PORT: the arguments after `context` are keyword-only in Python.
    // TODO(port): generate_prompt_response
    internal static Task<OrderedDictionary<string, object?>> GeneratePromptResponseAsync(
        LLMClient llmClient,
        string promptName,
        PromptFunction legacyPrompt,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        bool attributeExtraction = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): generate_prompt_response");
    }
}
