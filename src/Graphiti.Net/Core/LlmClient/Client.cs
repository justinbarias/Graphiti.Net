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

using System.Security.Cryptography;
using System.Text.Json;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

internal static partial class Client
{
    internal const string DefaultCacheDir = "./llm_cache";

    internal static ILogger Logger => PortServices.Logger(typeof(Client));

    /// <summary>
    /// Returns instruction for language extraction behavior.
    ///
    /// Override this function to customize language extraction:
    /// - Return empty string to disable multilingual instructions
    /// - Return custom instructions for specific language requirements
    /// - Use group_id to provide different instructions per group/partition
    ///
    /// Args:
    ///     group_id: Optional partition identifier for the graph
    ///
    /// Returns:
    ///     str: Language instruction to append to system messages.
    /// </summary>
    internal static string GetExtractionLanguageInstruction(string? groupId = null)
    {
        return
            "\n\nAny extracted information should be returned in the same language as it was written in. "
            + "Only output non-English text when the user has written full sentences or phrases in that non-English language. "
            + "Otherwise, output English.";
    }

    // PORT: the parameter has no annotation. Each caller is the retry predicate of tenacity, which passes the
    // exception of an attempt (R16, R20). `isinstance(exception, httpx.HTTPStatusError)` is an HttpRequestException
    // with a status code, and json.decoder.JSONDecodeError is JsonException (R4).
    internal static bool IsServerOrRetryError(Exception exception)
    {
        // EmptyResponseError is treated as transient: an empty body is most often a flaky
        // provider/endpoint hiccup (common on the OpenAI-compatible/local servers the generic
        // client targets), which a retry can recover from. A persistent empty response still
        // fails after the bounded retries with a clear error.
        if (exception is RateLimitException or EmptyResponseException or JsonException)
        {
            return true;
        }

        return exception is HttpRequestException { StatusCode: not null } status
            && (long)status.StatusCode >= 500 && (long)status.StatusCode < 600;
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Retrying _generate_response_with_retry after {AttemptNumber} attempts...")]
        internal static partial void RetryingAfterAttempts(ILogger logger, string attemptNumber);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Cache hit for {CacheKey}")]
        internal static partial void CacheHit(ILogger logger, string cacheKey);
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

    internal LLMClient(LLMConfig? config, bool cache = false)
    {
        if (config is null)
        {
            config = new LLMConfig();
        }

        Config = config;
        Model = config.Model;
        SmallModel = config.SmallModel;
        Temperature = config.Temperature;
        MaxTokens = config.MaxTokens;
        CacheEnabled = cache;
        CacheDir = null;
        Tracer = new NoOpTracer();
        TokenTracker = new TokenUsageTracker();

        // Only create the cache directory if caching is enabled
        if (CacheEnabled)
        {
            CacheDir = new LLMCache(Client.DefaultCacheDir);
        }
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

    /// <summary>Set the tracer for this LLM client.</summary>
    internal void SetTracer(Tracer tracer)
    {
        Tracer = tracer;
    }

    /// <summary>
    /// Clean input string of invalid unicode and control characters.
    ///
    /// Args:
    ///     input: Raw input string to be cleaned
    ///
    /// Returns:
    ///     Cleaned string safe for LLM processing.
    /// </summary>
    // PORT: private in Python and internal here, because a ported test reads it (R2). It does not read `self`, so it is
    // static: analyzer CA1822 is an error. A caller in a subclass writes the same name.
    internal static string CleanInput(string input)
    {
        // Clean any invalid Unicode
        var cleaned = PyText.DropLoneSurrogates(input);

        // Remove zero-width characters and other invisible unicode
        var zeroWidth = "\u200b\u200c\u200d\ufeff\u2060";
        foreach (var @char in PyText.CodePoints(zeroWidth))
        {
            cleaned = cleaned.Replace(@char, string.Empty, StringComparison.Ordinal);
        }

        // Remove control characters except newlines, returns, and tabs
        cleaned = string.Concat(PyText.CodePoints(cleaned).Where(@char => PyText.Ord(@char) >= 32 || "\n\r\t".Contains(@char, StringComparison.Ordinal)));

        return cleaned;
    }

    // PORT: `@retry(...)` is a Polly pipeline (R16). `model` is keyword-only in Python. Private in Python and internal
    // here, because the evaluation test of the retry (R23, J3) reads it (R2).
    // PORT: tenacity runs `after` for each attempt that fails with a retried error, also the last one, and it draws the
    // wait before the test of `stop`. Polly has no hook for the last attempt except the predicate, so the log is written
    // in ShouldHandle, after the predicate is true. Polly asks for no delay after the last attempt, so the draw of the
    // wait for that attempt has no form. No sleep is lost: tenacity does not sleep after it either.
    internal async Task<OrderedDictionary<string, object?>> GenerateResponseWithRetryAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var pipeline = new ResiliencePipelineBuilder<OrderedDictionary<string, object?>>
        {
            TimeProvider = PortServices.Time,
        }
        .AddRetry(new RetryStrategyOptions<OrderedDictionary<string, object?>>
        {
            // stop=stop_after_attempt(4): 4 attempts in total
            MaxRetryAttempts = 3,

            // retry=retry_if_exception(is_server_or_retry_error), and the `after` of tenacity
            ShouldHandle = args => new ValueTask<bool>(Asyncio.OnLoopAsync(() =>
            {
                var retry = args.Outcome.Exception is { } exception && Client.IsServerOrRetryError(exception);
                if (retry && args.AttemptNumber + 1 > 1)
                {
                    Client.Log.RetryingAfterAttempts(Client.Logger, Py.Str((long)args.AttemptNumber + 1));
                }

                return Task.FromResult(retry);
            })),

            // wait=wait_random_exponential(multiplier=10, min=5, max=120): wait_exponential gives
            // max(max(0, min), min(multiplier * 2 ** (attempt_number - 1), max)), and the wait is random.uniform(min, high).
            // The attempt number of tenacity counts from 1, and AttemptNumber of Polly counts from 0.
            DelayGenerator = args => new ValueTask<TimeSpan?>(Asyncio.OnLoopAsync(() =>
            {
                var high = Math.Max(Math.Max(0, 5), Math.Min(10 * Math.Pow(2, args.AttemptNumber), 120));

                // PORT: the delay of Polly has the resolution of a millisecond, and Python sleeps for the float.
                return Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(PortServices.Random.Uniform(5, high)));
            })),
        })
        .Build();

        // PORT: `except (httpx.HTTPStatusError, RateLimitError) as e: raise e` leaves the exception as it is, so it has no clause.
        var attempts = 0;
        try
        {
            return await pipeline.ExecuteAsync(
                async token => await Asyncio.OnLoopAsync(() =>
                {
                    attempts++;

                    // Only splat overrides when set so third-party subclasses with the
                    // pre-existing four-param signature still work on the legacy path.
                    return PrivateGenerateResponseAsync(messages, responseModel, maxTokens, modelSize, model, token);
                }),
                cancellationToken);
        }
        catch (Exception e) when (cancellationToken.IsCancellationRequested && attempts <= 3 && Client.IsServerOrRetryError(e))
        {
            // PORT: a cancel that arrives while an attempt fails with a retried error lands in the sleep of tenacity
            // (after the `after` log), and the call ends with CancelledError. Polly gives the last outcome for a canceled
            // token. After the last attempt tenacity does not sleep, so the error stays.
            throw new OperationCanceledException(cancellationToken);
        }
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

    // PORT: private in Python and internal here, because the recording of the cache key reads it (R2, R23).
    internal string GetCacheKey(List<Message> messages, string? model = null)
    {
        // Create a unique cache key based on the messages and model
        var messageStr = PyJson.Dumps(messages.Select(m => (object?)m.ModelDump()).ToList(), sortKeys: true);
        var keyStr = $"{Py.Str(Py.Truthy(model) ? model : Model)}:{messageStr}";
        // PORT: `hashlib.md5(..., usedforsecurity=False)`. The analyzer CA5351 is an error for MD5.HashData, which R18 names.
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(PyText.EncodeUtf8(keyStr));
        return Convert.ToHexStringLower(md5.GetHashAndReset());
    }

    /// <summary>
    /// Append a strict-framing instruction to the system message for attribute
    /// extraction calls.
    ///
    /// Customer-supplied entity-type schemas use ``Field(description=...)`` to describe
    /// the FORMAT of a value (e.g. "Phone numbers, comma-separated"). Models — across
    /// OpenAI, Anthropic, and Gemini — have been observed copying that description text
    /// verbatim into the value when no real value exists, or dumping reasoning instead
    /// of returning null. This preamble names the failure mode explicitly so the
    /// instruction reaches every provider regardless of how structured output is wired
    /// (schema injection in the prompt body, ``response_format``, native tool use, etc.).
    ///
    /// Mutates ``messages`` in place. Idempotent so concrete-provider overrides can
    /// safely call it without coordinating with the base class.
    /// </summary>
    // PORT: private in Python and internal here, because a ported test reads it (R2). It does not read `self`, so it is
    // static (CA1822).
    internal static void ApplyAttributeExtractionPreamble(List<Message> messages, bool attributeExtraction)
    {
        if (!attributeExtraction || messages.Count == 0)
        {
            return;
        }

        // Unique sentinel so the idempotency check can't collide with prompt content
        // that happens to mention "attribute extraction". Bump the suffix if the note
        // text is meaningfully revised so older callers don't suppress the new copy.
        var sentinel = "<<graphiti.attr_extraction.preamble.v1>>";
        var note =
            $"\n\n{sentinel}\n"
            + "ATTRIBUTE EXTRACTION: Field descriptions in the response schema describe "
            + "what a real value LOOKS LIKE — they are NEVER themselves valid values and "
            + "must NEVER be copied into any field. If you have no value for a field, set "
            + "it to null; never explain the absence in the field itself.";
        var target = messages[0];
        if (target.Content.Contains(sentinel, StringComparison.Ordinal))
        {
            return;
        }

        if (target.Role == "system")
        {
            target.Content += note;
        }
        else
        {
            target.Content = PyText.LStrip(note) + "\n\n" + target.Content;
        }
    }

    // PORT: `attribute_extraction` and `model` are keyword-only in Python.
    internal virtual async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
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
        maxTokens ??= MaxTokens;

        // The strict attribute-extraction framing belongs on the system message so it
        // reaches every provider regardless of structured-output mechanism.
        ApplyAttributeExtractionPreamble(messages, attributeExtraction);

        if (responseModel is not null)
        {
            var serializedModel = PyJson.Dumps(responseModel.ModelJsonSchema.ToDict());
            messages[^1].Content +=
                $"\n\nRespond with a JSON object in the following format:\n\n{serializedModel}";
        }

        // Add multilingual extraction instructions
        messages[0].Content += Client.GetExtractionLanguageInstruction(groupId);

        foreach (var message in messages)
        {
            message.Content = CleanInput(message.Content);
        }

        // Wrap entire operation in tracing span
        return await Tracer.StartSpanAsync<OrderedDictionary<string, object?>>(
            "llm.generate",
            async span =>
            {
                OrderedDictionary<string, object?> attributes = new()
                {
                    ["llm.provider"] = GetProviderType(),
                    ["model.size"] = Py.EnumValue(modelSize),
                    ["max_tokens"] = maxTokens.Value,
                    ["cache.enabled"] = CacheEnabled,
                };
                if (Py.Truthy(promptName))
                {
                    attributes["prompt.name"] = promptName;
                }

                span.AddAttributes(attributes);

                // Check cache first
                if (CacheEnabled && CacheDir is not null)
                {
                    var cacheKey = GetCacheKey(messages, model);
                    var cachedResponse = CacheDir.Get(cacheKey);
                    if (cachedResponse is not null)
                    {
                        Client.Log.CacheHit(Client.Logger, cacheKey);
                        span.AddAttributes(new OrderedDictionary<string, object?> { ["cache.hit"] = true });
                        return cachedResponse;
                    }
                }

                span.AddAttributes(new OrderedDictionary<string, object?> { ["cache.hit"] = false });

                // Execute LLM call
                OrderedDictionary<string, object?> response;
                try
                {
                    response = await GenerateResponseWithRetryAsync(
                        messages,
                        responseModel,
                        maxTokens.Value,
                        modelSize,
                        model,
                        cancellationToken);
                }
                catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
                {
                    span.SetStatus("error", e.Message);
                    span.RecordException(e);
                    throw;
                }

                // Cache response if enabled
                if (CacheEnabled && CacheDir is not null)
                {
                    var cacheKey = GetCacheKey(messages, model);
                    CacheDir.Set(cacheKey, response);
                }

                return response;
            },
            cancellationToken);
    }

    /// <summary>Get provider type from class name.</summary>
    internal virtual string GetProviderType()
    {
        var className = PyText.Lower(PythonName);
        if (className.Contains("openai", StringComparison.Ordinal))
        {
            return "openai";
        }
        else if (className.Contains("anthropic", StringComparison.Ordinal))
        {
            return "anthropic";
        }
        else if (className.Contains("gemini", StringComparison.Ordinal))
        {
            return "gemini";
        }
        else if (className.Contains("groq", StringComparison.Ordinal))
        {
            return "groq";
        }
        else
        {
            return "unknown";
        }
    }

    /// <summary>
    /// Log structural metadata and truncated raw output for debugging failed
    /// generations, without including full message content that may contain PII.
    /// </summary>
    // PORT: private in Python and internal here, because the recording reads it (R2). It does not read `self`, so it is
    // static (CA1822).
    internal static string GetFailedGenerationLog(List<Message> messages, string? output)
    {
        var log = $"Input messages: {Py.Str(Py.Len(messages))} message(s), ";
        log += $"roles: {Py.Repr(messages.Select(m => (object?)m.Role).ToList())}\n";
        if (output is not null)
        {
            var truncated = Py.Len(output) > 500 ? Py.Slice(output, null, 500) + "..." : output;
            log += $"Raw output (truncated): {truncated}\n";
        }
        else
        {
            log += "No raw output available";
        }

        return log;
    }
}
