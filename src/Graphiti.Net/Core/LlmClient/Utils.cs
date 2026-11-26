// Ported from graphiti_core/llm_client/utils.py
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

using Graphiti.Core.Embedder;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class Utils
{
    private static ILogger Logger => PortServices.Logger(typeof(Utils));

    // PORT: the result has no annotation. TYPE_HINTS.md gives list[float].
    internal static async Task<List<double>> GenerateEmbeddingAsync(
        EmbedderClient embedder,
        string text,
        CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();

        text = text.Replace("\n", " ", StringComparison.Ordinal);
        var embedding = await embedder.CreateAsync([text], cancellationToken);

        var end = Py.TimeSeconds();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var length = Py.Str(Py.Len(text));
            var elapsed = Py.Str(end - start);
            Log.EmbeddedText(logger, length, elapsed);
        }

        return embedding;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "embedded text of length {Length} in {Elapsed} ms")]
        internal static partial void EmbeddedText(ILogger logger, string length, string elapsed);
    }
}
