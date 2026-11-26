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
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module name equals the class name OpenAIRerankerClient, so the module class has the suffix Module (R2).
internal static partial class OpenaiRerankerClientModule
{
    // gpt-6-luna rejects max_tokens and logit_bias, so the logprob reranker uses a non-reasoning model.
    internal const string DefaultModel = "gpt-4.1-mini";

    // PORT: R5 puts the logger on the module class. The class that logs is OpenAIRerankerClient, so the members are internal.
    internal static ILogger Logger => PortServices.Logger(typeof(OpenaiRerankerClientModule));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Error in generating LLM response: {Error}")]
        internal static partial void ErrorInGeneratingLlmResponse(ILogger logger, string error);
    }
}

// PORT: `self.client` is the lead seam Graphiti.Internal.LogProbabilityChat (decision D30, R19). The branches that
// build AsyncOpenAI or unwrap an OpenAIClient are not ported, and a null client throws by the default-object rule
// of R1.
internal sealed class OpenAIRerankerClient : CrossEncoderClient
{
    internal OpenAIRerankerClient(LLMConfig? config = null, LogProbabilityChat? client = null)
    {
        config ??= new LLMConfig();

        Config = config;
        if (client is null)
        {
            throw new ArgumentException("client must be provided"); // PORT: D30, R19
        }

        Client = client;
    }

    internal LLMConfig Config { get; set; }

    internal LogProbabilityChat Client { get; set; }

    internal override async Task<List<(string, double)>> RankAsync(
        string query,
        List<string> passages,
        CancellationToken cancellationToken = default)
    {
        var openaiMessagesList = new List<List<Message>>();
        foreach (var passage in passages)
        {
            openaiMessagesList.Add(
            [
                new Message
                {
                    Role = "system",
                    Content = "You are an expert tasked with determining whether the passage is relevant to the query",
                },
                new Message
                {
                    // PORT: R9. The first line break and the last line break with its spaces are outside the literal.
                    Role = "user",
                    Content = "\n" + $$"""
                           Respond with "True" if PASSAGE is relevant to QUERY and "False" otherwise.
                           <PASSAGE>
                           {{passage}}
                           </PASSAGE>
                           <QUERY>
                           {{query}}
                           </QUERY>
""" + "\n                           ",
                },
            ]);
        }

        try
        {
            var responses = await Helpers.SemaphoreGatherAsync(
                [.. openaiMessagesList.Select<List<Message>, Func<Task<LogProbabilityChatResponse>>>(openaiMessages => () => Client.CreateAsync(
                    // PORT: `self.config.model or DEFAULT_MODEL`. A null model is the default of the chat client (R19).
                    string.IsNullOrEmpty(Config.Model) ? null : Config.Model,
                    openaiMessages,
                    temperature: 0,
                    maxTokens: 1,
                    logitBias: new OrderedDictionary<string, long> { ["6432"] = 1, ["7983"] = 1 },
                    logprobs: true,
                    topLogprobs: 2,
                    cancellationToken: cancellationToken))],
                cancellationToken: cancellationToken);

            // PORT: R19. The reads of the Python lines 97 to 101, one by one: `choices[0].logprobs.content[0].top_logprobs`
            // when `logprobs` and `content` are not None.
            var responsesTopLogprobs = new List<List<LogProbabilityTopLogprob>>();
            foreach (var response in responses)
            {
                responsesTopLogprobs.Add(
                    response.Choices[0].Logprobs is not null
                    && response.Choices[0].Logprobs!.Content is not null
                        ? response.Choices[0].Logprobs!.Content![0].TopLogprobs
                        : []);
            }

            var scores = new List<double>();
            foreach (var topLogprobs in responsesTopLogprobs)
            {
                if (topLogprobs.Count == 0)
                {
                    continue;
                }

                var normLogprobs = Math.Exp(topLogprobs[0].Logprob);
                if (PyText.Lower(PyText.Split(PyText.Strip(topLogprobs[0].Token), " ")[0]) == "true")
                {
                    scores.Add(normLogprobs);
                }
                else
                {
                    scores.Add(1 - normLogprobs);
                }
            }

            // PORT: zip(passages, scores, strict=True). Py.ZipStrict does not exist. `scores` is never longer than
            // `passages`, so the only error is "zip() argument 2 is shorter than argument 1", and it comes before a pair is built.
            if (scores.Count != passages.Count)
            {
                throw new ArgumentException(scores.Count < passages.Count
                    ? "zip() argument 2 is shorter than argument 1"
                    : "zip() argument 2 is longer than argument 1");
            }

            var results = new List<(string, double)>();
            for (var i = 0; i < passages.Count; i++)
            {
                results.Add((passages[i], scores[i]));
            }

            Py.SortInPlace(results, x => x.Item2, reverse: true);
            return results;
        }
        catch (Exception e) when (PortServices.ClassifyModelFailure(e) == ModelCallFailure.RateLimit)
        {
            throw new RateLimitException("Rate limit exceeded. Please try again later.", e);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            OpenaiRerankerClientModule.Log.ErrorInGeneratingLlmResponse(OpenaiRerankerClientModule.Logger, e.Message);
            throw;
        }
    }
}
