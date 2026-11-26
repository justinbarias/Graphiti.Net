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

using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text.Json;
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

/// <summary>Raised when one Decisions question cannot fit after state shrinking.</summary>
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

/// <summary>LLM client for OpenAI Decisions node dedupe and edge resolution.</summary>
// PORT: `httpx.AsyncClient` is an HttpClient that the constructor takes (decision D32, R19). The arguments after
// `config` are keyword-only in Python. The module class of config.py has the name of the property `Config`, so a
// default value names it with the full namespace.
internal sealed class OpenAIDecisionsClient : LLMClient
{
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
        // PORT: `api_key` is the resolved key, which ResolveConfig puts in the config that the base class keeps. The
        // `?? string.Empty` only gives the non-null type, because the key is never empty here.
        ApiKey = Config.ApiKey ?? string.Empty;
        BaseUrl = PyText.RStrip(
            !string.IsNullOrEmpty(baseUrl) ? baseUrl
            : !string.IsNullOrEmpty(Config.BaseUrl) ? Config.BaseUrl
            : PyEnv.Str("OPENAI_BASE_URL") is { Length: > 0 } environmentBaseUrl ? environmentBaseUrl
            : "https://api.openai.com/v1",
            "/");
        DuplicateThreshold = duplicateThreshold;
        ContradictionThreshold = contradictionThreshold;
        TimeoutSeconds = timeoutSeconds;
        MaxRetries = maxRetries;
        HttpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
    }

    // PORT: the config that Python builds before super().__init__ (R4).
    private static LLMConfig ResolveConfig(LLMConfig? config)
    {
        var sourceConfig = config ?? new LLMConfig();
        var apiKey = !string.IsNullOrEmpty(sourceConfig.ApiKey) ? sourceConfig.ApiKey : PyEnv.Str("OPENAI_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("OPENAI_API_KEY must be set or provided in LLMConfig.api_key");
        }

        var resolvedConfig = new LLMConfig(
            apiKey: apiKey,
            model: !string.IsNullOrEmpty(sourceConfig.Model) ? sourceConfig.Model : "gpt-6-luna",
            baseUrl: sourceConfig.BaseUrl,
            temperature: sourceConfig.Temperature,
            maxTokens: sourceConfig.MaxTokens,
            smallModel: sourceConfig.SmallModel);
        return resolvedConfig;
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

    internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
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
        var nodePrompt = "dedupe_nodes.nodes";
        var edgePrompt = "dedupe_edges.resolve_edge";
        if (promptName != nodePrompt && promptName != edgePrompt)
        {
            throw new ArgumentException(
                "OpenAIDecisionsClient cannot serve prompt " + Py.Repr(promptName) + "; it serves only "
                + "dedupe_nodes.nodes and dedupe_edges.resolve_edge via "
                + "openai_decisions_prompt_overrides().");
        }

        var payload = Py.AsDict(PyJson.Loads(DecisionPrompts.UserText(messages)));
        var instructions = Py.Get(payload, "instructions", "");
        var state = payload["state"];
        var questionsValue = payload["questions"];
        if (instructions is not string instructionsText || state is not string stateText)
        {
            throw new ArgumentException("Decision prompt instructions and state must be strings");
        }

        if (questionsValue is not OrderedDictionary<string, object?> questionsDict)
        {
            throw new ArgumentException("Decision prompt questions must be an object");
        }

        // PORT: `questions: dict[str, dict[str, Any]]`. A question that is not a dict throws here, where Python
        // fails at its first read of the question.
        var questions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
        foreach (var (key, value) in questionsDict)
        {
            questions[key] = Py.AsDict(value);
        }

        var modelId = !string.IsNullOrEmpty(model) ? model : Model;
        if (modelId is null)
        {
            throw new ArgumentException("OpenAI Decisions model must be set");
        }

        var entities = promptName == nodePrompt ? DecisionPrompts.ParseList(DecisionPrompts.Tag(stateText, "ENTITIES")) : [];

        // PORT: int(name[3:]) of text has no `int` of the value model, and the names are text (R18).
        var existingIndices = promptName == edgePrompt
            ? questions.Keys.Where(name => name.StartsWith("dup", StringComparison.Ordinal)).Select(name => Py.Int(Py.Slice(name, 3, null))).Order().ToList()
            : [];
        var contradictionIndices = promptName == edgePrompt
            ? questions.Keys.Where(name => name.StartsWith("con", StringComparison.Ordinal)).Select(name => Py.Int(Py.Slice(name, 3, null))).Order().ToList()
            : [];
        var usage = new Dictionary<string, long> { ["input_tokens"] = 0, ["output_tokens"] = 0 };
        maxTokens ??= MaxTokens;

        return await Tracer.StartSpanAsync<OrderedDictionary<string, object?>>(
            "llm.generate",
            async span =>
            {
                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["llm.provider"] = "openai",
                    ["model.size"] = Py.EnumValue(modelSize),
                    ["max_tokens"] = maxTokens,
                    ["cache.enabled"] = CacheEnabled,
                    ["prompt.name"] = promptName,
                    ["cache.hit"] = false,
                });
                try
                {
                    var answers = questions.Count > 0
                        ? await CallFitAsync(instructionsText, stateText, questions, modelId, usage, cancellationToken)
                        : [];
                    if (promptName == nodePrompt)
                    {
                        return DecisionPrompts.AssembleNodeAnswers(answers, entities);
                    }

                    return DecisionPrompts.AssembleEdgeAnswers(
                        answers,
                        existingIndices,
                        contradictionIndices,
                        DuplicateThreshold,
                        ContradictionThreshold);
                }
                catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
                {
                    span.SetStatus("error", error.Message);
                    span.RecordException(error);
                    throw;
                }
                finally
                {
                    TokenTracker.Record(promptName, usage["input_tokens"], usage["output_tokens"]);
                }
            },
            cancellationToken);
    }

    // PORT: `questions: dict[str, dict[str, Any]]`.
    private async Task<OrderedDictionary<string, object?>> PostAsync(
        string instructions,
        string state,
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        string model,
        CancellationToken cancellationToken = default)
    {
        var inputText = string.Join(
            "\n\n",
            new[] { instructions, state }.Where(part => PyText.Strip(part) != string.Empty).Select(part => PyText.RStrip(part, "\n")));
        List<object?> wireQuestions = [];
        foreach (var (name, question) in questions)
        {
            var questionType = Py.Get(question, "type");
            if (questionType is "choice")
            {
                var criteria = Py.Get(question, "criteria");
                if (criteria is not OrderedDictionary<string, object?> criteriaDict)
                {
                    throw new ArgumentException("Choice question " + Py.Repr(name) + " must have criteria");
                }

                var instructionsValue = Py.Get(question, "instructions");
                List<object?> choices = [];
                foreach (var (value, description) in criteriaDict)
                {
                    choices.Add(new OrderedDictionary<string, object?> { ["value"] = value, ["description"] = Py.Str(description) });
                }

                wireQuestions.Add(new OrderedDictionary<string, object?>
                {
                    ["name"] = name,
                    ["type"] = "choice",
                    ["instructions"] = Py.Truthy(instructionsValue) ? instructionsValue : string.Empty,
                    ["choices"] = choices,
                });
            }
            else if (questionType is "predicate" or "noul")
            {
                var instructionsValue = Py.Get(question, "instructions");
                wireQuestions.Add(new OrderedDictionary<string, object?>
                {
                    ["name"] = name,
                    ["type"] = "predicate",
                    ["instructions"] = Py.Truthy(instructionsValue) ? instructionsValue : string.Empty,
                });
            }
            else
            {
                throw new ArgumentException("Unsupported decision question type " + Py.Repr(questionType));
            }
        }

        var requestBody = new OrderedDictionary<string, object?> { ["model"] = model, ["input"] = inputText, ["questions"] = wireQuestions };
        Exception? lastError = null;
        for (long attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/decisions");
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ApiKey);

                // PORT: R19. httpx writes `json=` with ensure_ascii=False, the separators `,` and `:`, and allow_nan=False.
                request.Content = new ByteArrayContent(PyText.EncodeUtf8(PyJson.Dumps(requestBody, ensureAscii: false, compact: true, allowNan: false)));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                using var response = await Asyncio.WaitForAsync(
                    token => HttpClient.SendAsync(request, token),
                    TimeoutSeconds,
                    PortServices.Time,
                    cancellationToken);
                if (IsInputTooLong(response))
                {
                    throw new OpenAIInputTooLongSignalException(await response.Content.ReadAsStringAsync(cancellationToken));
                }

                var statusCode = (long)response.StatusCode;
                if (!(200 <= statusCode && statusCode < 300))
                {
                    throw new HttpRequestException(
                        "HTTP " + Py.Str(statusCode) + ": " + Py.Slice(await response.Content.ReadAsStringAsync(cancellationToken), null, 500),
                        null,
                        response.StatusCode);
                }

                var result = Py.AsDict(PyJson.Loads(await response.Content.ReadAsStringAsync(cancellationToken)));
                var answers = MapAnswers(questions, Py.Get(result, "answers"));
                var usageValue = Py.Get(result, "usage");
                var usage = Py.AsDict(Py.Truthy(usageValue) ? usageValue : new OrderedDictionary<string, object?>());

                // PORT: int(x) of a value from open data has no PyCompat helper for an object (reported).
                return new OrderedDictionary<string, object?>
                {
                    ["answers"] = answers,
                    ["usage"] = new OrderedDictionary<string, object?>
                    {
                        ["input_tokens"] = Py.Get(usage, "input_tokens", 0L) switch
                        {
                            long l => l,
                            string s => Py.Int(s),
                            double d => Py.Int(d),
                            bool b => b ? 1L : 0L,
                            _ => throw new InvalidCastException("int() argument must be a string, a bytes-like object or a real number"),
                        },
                        ["output_tokens"] = Py.Get(usage, "output_tokens", 0L) switch
                        {
                            long l => l,
                            string s => Py.Int(s),
                            double d => Py.Int(d),
                            bool b => b ? 1L : 0L,
                            _ => throw new InvalidCastException("int() argument must be a string, a bytes-like object or a real number"),
                        },
                    },
                };
            }
            catch (OpenAIInputTooLongSignalException)
            {
                throw;
            }
            catch (Exception error) when (error is TimeoutException
                || (error is OperationCanceledException && !Asyncio.IsCancellation(error, cancellationToken))
                || error is HttpRequestException { StatusCode: null } or IOException)
            {
                lastError = error;
            }
            catch (HttpRequestException error) when (error.StatusCode is not null)
            {
                var status = (long)error.StatusCode.GetValueOrDefault();
                if (status != 429 && status < 500)
                {
                    throw;
                }

                lastError = error;
            }

            if (attempt < MaxRetries - 1)
            {
                await OpenaiDecisionsClientModule.Sleep.Value(Math.Pow(2, attempt), cancellationToken);
            }
        }

        if (lastError is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
        {
            throw new RateLimitException(lastError.Message, lastError);
        }

        if (lastError is not null)
        {
            throw lastError;
        }

        throw new InvalidOperationException("OpenAI Decisions request did not run; max_retries must be greater than zero");
    }

    // PORT: the body is buffered after PostAsync, so response.Content.ReadAsStream() reads it with no wait (R19).
    private static bool IsInputTooLong(HttpResponseMessage response)
    {
        if ((long)response.StatusCode != 400)
        {
            return false;
        }

        object? error;
        try
        {
            using var stream = response.Content.ReadAsStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            // PORT: `response.json().get('error', {})`. A body that is not a dict is an AttributeError in Python, and
            // Py.AsDict throws InvalidCastException for it.
            error = Py.Get(Py.AsDict(PyJson.Loads(PyText.DecodeUtf8(buffer.ToArray()))), "error", new OrderedDictionary<string, object?>());
        }
        catch (Exception e) when (e is ArgumentException or JsonException or InvalidCastException)
        {
            return false;
        }

        if (error is not OrderedDictionary<string, object?> errorDict)
        {
            return false;
        }

        if (Py.Get(errorDict, "code") is "context_length_exceeded" or "string_above_max_length")
        {
            return true;
        }

        var message = Py.Get(errorDict, "message");
        return Py.Get(errorDict, "type") is "invalid_request_error"
            && message is string messageText
            && PyText.Lower(messageText).Contains("exceeds the token limit", StringComparison.Ordinal);
    }

    // PORT: a classmethod is a static method (R16). `raw_answers: Any` is a value of the value model (R6).
    internal static OrderedDictionary<string, object?> MapAnswers(
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        object? rawAnswers)
    {
        if (rawAnswers is not List<object?> rawAnswerList)
        {
            throw new ArgumentException("OpenAI Decisions response must contain an answers list");
        }

        OrderedDictionary<string, object?> mapped = [];
        foreach (var answerValue in rawAnswerList)
        {
            if (answerValue is not OrderedDictionary<string, object?> answer)
            {
                throw new ArgumentException("OpenAI Decisions answer must be an object");
            }

            var name = Py.Get(answer, "name");
            OrderedDictionary<string, object?>? question = null;
            if (name is not string nameText || !questions.TryGetValue(nameText, out question))
            {
                throw new ArgumentException("OpenAI Decisions returned an unknown answer name " + Py.Repr(name));
            }

            if (mapped.ContainsKey(nameText))
            {
                throw new ArgumentException("OpenAI Decisions returned duplicate answer " + Py.Repr(nameText));
            }

            var expectedType = Py.Get(question, "type") is "choice" ? "choice" : "predicate";
            if (Py.Get(answer, "type") is not string answerType || answerType != expectedType)
            {
                throw new ArgumentException(
                    "OpenAI Decisions answer " + Py.Repr(nameText) + " has type " + Py.Repr(Py.Get(answer, "type")) + "; "
                    + "expected " + Py.Repr(expectedType));
            }

            if (expectedType == "predicate")
            {
                var probability = Py.Get(answer, "probability");
                if (!ValidProbability(probability))
                {
                    throw new ArgumentException("OpenAI Decisions predicate answer " + Py.Repr(nameText) + " needs a valid probability");
                }

                mapped[nameText] = new OrderedDictionary<string, object?> { ["noul"] = Py.AsDouble(probability) };
                continue;
            }

            var criteria = Py.Get(question, "criteria");
            if (criteria is not OrderedDictionary<string, object?> criteriaDict)
            {
                throw new ArgumentException("Choice question " + Py.Repr(nameText) + " must have criteria");
            }

            var allowed = new HashSet<string>(criteriaDict.Keys, StringComparer.Ordinal);
            var choice = Py.Get(answer, "choice");
            if (choice is not null && (choice is not string choiceText || !allowed.Contains(choiceText)))
            {
                throw new ArgumentException("OpenAI Decisions choice " + Py.Repr(choice) + " is not allowed for answer " + Py.Repr(nameText));
            }

            var probabilities = Py.Get(answer, "probabilities");
            if (probabilities is List<object?> { Count: > 0 } probabilityList)
            {
                OrderedDictionary<string, object?> mappedProbabilities = [];
                foreach (var itemValue in probabilityList)
                {
                    if (itemValue is not OrderedDictionary<string, object?> item)
                    {
                        throw new ArgumentException("OpenAI Decisions probability for " + Py.Repr(nameText) + " must be an object");
                    }

                    var value = Py.Get(item, "value");
                    var probability = Py.Get(item, "probability");
                    if (value is not string valueText || !allowed.Contains(valueText))
                    {
                        throw new ArgumentException("OpenAI Decisions choice " + Py.Repr(value) + " is not allowed for answer " + Py.Repr(nameText));
                    }

                    if (mappedProbabilities.ContainsKey(valueText) || !ValidProbability(probability))
                    {
                        throw new ArgumentException("OpenAI Decisions probability for choice " + Py.Repr(valueText) + " is not valid");
                    }

                    mappedProbabilities[valueText] = Py.AsDouble(probability);
                }

                mapped[nameText] = new OrderedDictionary<string, object?> { ["probabilities"] = mappedProbabilities };
            }
            else
            {
                var confidence = Py.Get(answer, "confidence");
                if (choice is not string choiceKey || !ValidProbability(confidence))
                {
                    throw new ArgumentException(
                        "OpenAI Decisions choice answer " + Py.Repr(nameText) + " needs probabilities or "
                        + "a choice with confidence");
                }

                mapped[nameText] = new OrderedDictionary<string, object?>
                {
                    ["choice"] = choiceKey,
                    ["probabilities"] = new OrderedDictionary<string, object?> { [choiceKey] = Py.AsDouble(confidence) },
                };
            }
        }

        var missing = questions.Keys.Where(key => !mapped.ContainsKey(key)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                "OpenAI Decisions response is missing answers for "
                + Py.Repr(missing.Order(Py.StringComparer).Select(key => (object?)key).ToList()));
        }

        return mapped;
    }

    // PORT: `TypeGuard[int | float]` is a bool result. A bool is not a long in C#, so it needs no test.
    private static bool ValidProbability(object? value)
    {
        if (value is not (long or double or BigInteger))
        {
            return false;
        }

        var number = Py.AsDouble(value);
        return 0.0 <= number && number <= 1.0;
    }

    // PORT: `usage: dict[str, int]` is read and written only by key (R14).
    private async Task<OrderedDictionary<string, object?>> CallFitAsync(
        string instructions,
        string state,
        OrderedDictionary<string, OrderedDictionary<string, object?>> questions,
        string model,
        Dictionary<string, long> usage,
        CancellationToken cancellationToken = default)
    {
        OrderedDictionary<string, object?> response;
        try
        {
            response = await PostAsync(instructions, state, questions, model, cancellationToken);
        }
        catch (OpenAIInputTooLongSignalException error)
        {
            var keys = questions.Keys.ToList();
            if (keys.Count > 1)
            {
                var middle = Py.FloorDiv(keys.Count, 2);
                var firstQuestions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
                foreach (var key in Py.Slice(keys, null, middle))
                {
                    firstQuestions[key] = questions[key];
                }

                var first = await CallFitAsync(instructions, state, firstQuestions, model, usage, cancellationToken);
                var secondQuestions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
                foreach (var key in Py.Slice(keys, middle, null))
                {
                    secondQuestions[key] = questions[key];
                }

                var second = await CallFitAsync(instructions, state, secondQuestions, model, usage, cancellationToken);

                // {**first, **second}: a key of `first` keeps its position and takes the value of `second` (R6).
                var merged = new OrderedDictionary<string, object?>();
                foreach (var (key, value) in first)
                {
                    merged[key] = value;
                }

                foreach (var (key, value) in second)
                {
                    merged[key] = value;
                }

                return merged;
            }

            var smaller = DecisionPrompts.ShrinkState(state);
            if (smaller is null)
            {
                throw new OpenAIDecisionsInputTooLongException("OpenAI Decisions could not fit one question after shrinking the state.", error);
            }

            return await CallFitAsync(instructions, smaller, questions, model, usage, cancellationToken);
        }

        var responseUsageValue = Py.Get(response, "usage");
        var responseUsage = Py.AsDict(Py.Truthy(responseUsageValue) ? responseUsageValue : new OrderedDictionary<string, object?>());

        // PORT: int(x) of a value from open data has no PyCompat helper for an object (reported).
        usage["input_tokens"] += Py.Get(responseUsage, "input_tokens", 0L) switch
        {
            long l => l,
            string s => Py.Int(s),
            double d => Py.Int(d),
            bool b => b ? 1L : 0L,
            _ => throw new InvalidCastException("int() argument must be a string, a bytes-like object or a real number"),
        };
        usage["output_tokens"] += Py.Get(responseUsage, "output_tokens", 0L) switch
        {
            long l => l,
            string s => Py.Int(s),
            double d => Py.Int(d),
            bool b => b ? 1L : 0L,
            _ => throw new InvalidCastException("int() argument must be a string, a bytes-like object or a real number"),
        };
        return Py.AsDict(response["answers"]);
    }
}

internal static partial class OpenaiDecisionsClientModule
{
    /// <summary>Return the shared n6 and e2 prompt overrides for OpenAI Decisions.</summary>
    internal static LLMPromptOverrides OpenaiDecisionsPromptOverrides(IPromptLibrary? library = null)
    {
        return DecisionPrompts.JevPromptOverrides(library);
    }
}
