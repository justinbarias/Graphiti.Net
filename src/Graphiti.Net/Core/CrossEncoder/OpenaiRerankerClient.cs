// Ported from graphiti_core/cross_encoder/openai_reranker_client.py
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

namespace Graphiti.Core.CrossEncoder;

using Graphiti.Core.LlmClient;
using Graphiti.Internal;

// PORT: the module name equals the class name OpenAIRerankerClient, so the module class has the suffix Module (R2).
internal static class OpenaiRerankerClientModule
{
    // gpt-6-luna rejects max_tokens and logit_bias, so the logprob reranker uses a non-reasoning model.
    internal const string DefaultModel = "gpt-4.1-mini";
}

// PORT: `self.client` is the lead seam Graphiti.Internal.LogProbabilityChat (decision D30, R19). The branches that
// build AsyncOpenAI or unwrap an OpenAIClient are not ported, and a null client throws by the default-object rule
// of R1.
internal sealed class OpenAIRerankerClient : CrossEncoderClient
{
    // TODO(port): OpenAIRerankerClient.__init__
    internal OpenAIRerankerClient(LLMConfig? config = null, LogProbabilityChat? client = null)
    {
        throw new NotSupportedException("TODO(port): OpenAIRerankerClient.__init__");
    }

    internal LLMConfig Config { get; set; }

    internal LogProbabilityChat Client { get; set; }

    // TODO(port): OpenAIRerankerClient.rank
    internal override Task<List<(string, double)>> RankAsync(
        string query,
        List<string> passages,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): OpenAIRerankerClient.rank");
    }
}
