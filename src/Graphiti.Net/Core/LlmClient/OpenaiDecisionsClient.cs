// Ported from graphiti_core/llm_client/openai_decisions_client.py
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

// PORT: the module name equals the class name OpenAIDecisionsClient, so the module class has the suffix Module (R2).
// It is split around the classes to keep the order of the Python file.
internal static partial class OpenaiDecisionsClientModule
{
    // PORT: an upstream test replaces _sleep (R17).
    internal static readonly PyPatch<Func<double, CancellationToken, Task>> Sleep =
        new((seconds, cancellationToken) => Task.Delay(TimeSpan.FromSeconds(seconds), PortServices.Time, cancellationToken));
}

// PORT: both exception classes stay internal in the file of their module (decision D32, R4). A class with no
// __init__ takes `string message = ""`. The constructors only pass the message on, so they are ported in full.
internal sealed class OpenAIDecisionsInputTooLongException : Exception
{
    internal OpenAIDecisionsInputTooLongException(string message = "")
        : this(message, null)
    {
    }

    internal OpenAIDecisionsInputTooLongException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class OpenAIInputTooLongSignalException : Exception
{
    internal OpenAIInputTooLongSignalException(string message = "")
        : this(message, null)
    {
    }

    internal OpenAIInputTooLongSignalException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

// PORT: `httpx.AsyncClient` is an HttpClient that the constructor takes (decision D32, R19). The arguments after
// `config` are keyword-only in Python. The module class of config.py has the name of the property `Config`, so a
// default value names it with the full namespace.
internal sealed class OpenAIDecisionsClient : LLMClient
{
    // TODO(port): OpenAIDecisionsClient.__init__
    internal OpenAIDecisionsClient(
        LLMConfig? config = null,
        string? baseUrl = null,
        double duplicateThreshold = 0.7,
        double contradictionThreshold = 0.8,
        double timeoutSeconds = 120.0,
        long maxRetries = 5,
        HttpClient? httpClient = null)
        : base(ResolveConfig(config))
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient.__init__");
    }

    // TODO(port): OpenAIDecisionsClient.__init__ (the config that Python builds before super().__init__, R4)
    private static LLMConfig ResolveConfig(LLMConfig? config)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient.__init__ config");
    }

    // PORT: type(self).__name__ (R15).
    internal override string PythonName => "OpenAIDecisionsClient";

    internal string ApiKey { get; set; }

    internal string BaseUrl { get; set; }

    internal double DuplicateThreshold { get; set; }

    internal double ContradictionThreshold { get; set; }

    internal double TimeoutSeconds { get; set; }

    internal long MaxRetries { get; set; }

    internal HttpClient HttpClient { get; set; }

    // PORT: the body only raises NotImplementedError, so it is ported in full (R24).
    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("OpenAIDecisionsClient requires a dedupe prompt_name through LLMRuntime");
    }

    // TODO(port): OpenAIDecisionsClient.generate_response
    internal override Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
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
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient.generate_response");
    }

    // PORT: `questions: dict[str, dict[str, Any]]`.
    // TODO(port): OpenAIDecisionsClient._post
    private Task<OrderedDictionary<string, object?>> PostAsync(
        string instructions,
        string state,
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        string model,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient._post");
    }

    // TODO(port): no rule for a sync read of the body of `httpx.Response`. httpx holds the body after the request,
    // and an HttpResponseMessage gives it only through an async read. The other form adds the body text as a
    // second parameter, which _post reads one time.
    private static bool IsInputTooLong(HttpResponseMessage response)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient._is_input_too_long");
    }

    // PORT: a classmethod is a static method (R16). `raw_answers: Any` is a value of the value model (R6).
    // TODO(port): OpenAIDecisionsClient._map_answers
    internal static OrderedDictionary<string, object?> MapAnswers(
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        object? rawAnswers)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient._map_answers");
    }

    // PORT: `TypeGuard[int | float]` is a bool result.
    // TODO(port): OpenAIDecisionsClient._valid_probability
    private static bool ValidProbability(object? value)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient._valid_probability");
    }

    // PORT: `usage: dict[str, int]` is read and written only by key (R14).
    // TODO(port): OpenAIDecisionsClient._call_fit
    private Task<OrderedDictionary<string, object?>> CallFitAsync(
        string instructions,
        string state,
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        string model,
        Dictionary<string, long> usage,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): OpenAIDecisionsClient._call_fit");
    }
}

internal static partial class OpenaiDecisionsClientModule
{
    // TODO(port): openai_decisions_prompt_overrides
    internal static LLMPromptOverrides OpenaiDecisionsPromptOverrides(IPromptLibrary? library = null)
    {
        throw new NotSupportedException("TODO(port): openai_decisions_prompt_overrides");
    }
}
