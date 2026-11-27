// Ported from graphiti_core/llm_client/config.py
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

using Graphiti.PyCompat;

internal static class Config
{
    internal const long DefaultMaxTokens = 16384;

    internal const long DefaultTemperature = 1;
}

internal enum ModelSize
{
    [PyEnumMember("small", "small")]
    Small,
    [PyEnumMember("medium", "medium")]
    Medium,
}

/// <summary>
/// Configuration class for the Language Learning Model (LLM).
///
/// This class encapsulates the necessary parameters to interact with an LLM API,
/// such as OpenAI's GPT models. It stores the API key, model name, and base URL
/// for making requests to the LLM service.
/// </summary>
// PORT: a plain class whose __init__ sets six attributes. Each attribute is a property in the order of the
// assignments.
internal sealed class LLMConfig
{
    /// <summary>
    /// Initialize the LLMConfig with the provided parameters.
    /// </summary>
    /// <param name="apiKey">The authentication key for accessing the LLM API. This is required for making authorized requests.</param>
    /// <param name="model">The specific LLM model to use for generating responses. Defaults to "gpt-6-luna".</param>
    /// <param name="baseUrl">The base URL of the LLM API service. Defaults to "https://api.openai.com", which is OpenAI's standard API endpoint. This can be changed if using a different provider or a custom endpoint.</param>
    /// <param name="temperature">The sampling temperature.</param>
    /// <param name="maxTokens">The maximum number of tokens of a response.</param>
    /// <param name="smallModel">The specific LLM model to use for generating responses of simpler prompts. Defaults to "gpt-6-luna".</param>
    internal LLMConfig(
        string? apiKey = null,
        string? model = null,
        string? baseUrl = null,
        double temperature = Config.DefaultTemperature,
        long maxTokens = Config.DefaultMaxTokens,
        string? smallModel = null)
    {
        BaseUrl = baseUrl;
        ApiKey = apiKey;
        Model = model;
        SmallModel = smallModel;
        Temperature = temperature;
        MaxTokens = maxTokens;
    }

    internal string? BaseUrl { get; set; }

    internal string? ApiKey { get; set; }

    internal string? Model { get; set; }

    internal string? SmallModel { get; set; }

    internal double Temperature { get; set; }

    internal long MaxTokens { get; set; }
}
