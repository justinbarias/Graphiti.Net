// Ported from graphiti_core/llm_client/client.py
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

internal static class Client
{
    internal const string DefaultCacheDir = "./llm_cache";

    // TODO(port): get_extraction_language_instruction
    internal static string GetExtractionLanguageInstruction(string? groupId = null)
    {
        throw new NotSupportedException("TODO(port): get_extraction_language_instruction");
    }

    // PORT: the parameter has no annotation. Each caller is the retry predicate of tenacity, which passes the
    // exception of an attempt (R16, R20).
    // TODO(port): is_server_or_retry_error
    internal static bool IsServerOrRetryError(Exception exception)
    {
        throw new NotSupportedException("TODO(port): is_server_or_retry_error");
    }
}

// PORT: the seam of the chat adapter (decision D17, the public surface design). ChatClientLlmClient in
// Graphiti.Internal derives from this class, overrides PrivateGenerateResponseAsync (`_generate_response`), and also
// overrides GenerateResponseAsync (`generate_response`) and GetProviderType (`_get_provider_type`), so those two are
// virtual. The module class of config.py has the name of the property `Config`, so a default value names it with
// the full namespace.
internal abstract class LLMClient
{
    // PORT: type(self).__name__ (R15).
    internal abstract string PythonName { get; }

    // TODO(port): LLMClient.__init__
    internal LLMClient(LLMConfig? config, bool cache = false)
    {
        throw new NotSupportedException("TODO(port): LLMClient.__init__");
    }

    internal LLMConfig Config { get; set; }

    internal string? Model { get; set; }

    internal string? SmallModel { get; set; }

    internal double Temperature { get; set; }

    internal long MaxTokens { get; set; }

    internal bool CacheEnabled { get; set; }

    internal LLMCache? CacheDir { get; set; }

    internal Tracer Tracer { get; set; }

    internal TokenUsageTracker TokenTracker { get; set; }

    // TODO(port): LLMClient.set_tracer
    internal void SetTracer(Tracer tracer)
    {
        throw new NotSupportedException("TODO(port): LLMClient.set_tracer");
    }

    // TODO(port): LLMClient._clean_input
    internal string CleanInput(string input)
    {
        throw new NotSupportedException("TODO(port): LLMClient._clean_input");
    }

    // PORT: `@retry(...)` is a Polly pipeline (R16). `model` is keyword-only in Python.
    // TODO(port): LLMClient._generate_response_with_retry
    private Task<OrderedDictionary<string, object?>> GenerateResponseWithRetryAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): LLMClient._generate_response_with_retry");
    }

    // PORT: the prefix Private (R2). `_generate_response` without the underscore is the name of
    // `generate_response` in the same class. An abstract method has no body, as in
    // Python.
    internal abstract Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default);

    // TODO(port): LLMClient._get_cache_key
    internal string GetCacheKey(List<Message> messages, string? model = null)
    {
        throw new NotSupportedException("TODO(port): LLMClient._get_cache_key");
    }

    // TODO(port): LLMClient._apply_attribute_extraction_preamble
    internal void ApplyAttributeExtractionPreamble(List<Message> messages, bool attributeExtraction)
    {
        throw new NotSupportedException("TODO(port): LLMClient._apply_attribute_extraction_preamble");
    }

    // PORT: `attribute_extraction` and `model` are keyword-only in Python.
    // TODO(port): LLMClient.generate_response
    internal virtual Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): LLMClient.generate_response");
    }

    // TODO(port): LLMClient._get_provider_type
    internal virtual string GetProviderType()
    {
        throw new NotSupportedException("TODO(port): LLMClient._get_provider_type");
    }

    // TODO(port): LLMClient._get_failed_generation_log
    internal string GetFailedGenerationLog(List<Message> messages, string? output)
    {
        throw new NotSupportedException("TODO(port): LLMClient._get_failed_generation_log");
    }
}
