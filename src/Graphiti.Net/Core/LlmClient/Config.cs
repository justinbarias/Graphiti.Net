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

// PORT: a plain class whose __init__ sets six attributes. Each attribute is a property in the order of the
// assignments.
internal sealed class LLMConfig
{
    // TODO(port): LLMConfig.__init__
    internal LLMConfig(
        string? apiKey = null,
        string? model = null,
        string? baseUrl = null,
        double temperature = Config.DefaultTemperature,
        long maxTokens = Config.DefaultMaxTokens,
        string? smallModel = null)
    {
        throw new NotSupportedException("TODO(port): LLMConfig.__init__");
    }

    internal string? BaseUrl { get; set; }

    internal string? ApiKey { get; set; }

    internal string? Model { get; set; }

    internal string? SmallModel { get; set; }

    internal double Temperature { get; set; }

    internal long MaxTokens { get; set; }
}
