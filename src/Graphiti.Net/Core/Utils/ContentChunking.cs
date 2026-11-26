// Ported from graphiti_core/utils/content_chunking.py
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

namespace Graphiti.Core.Utils;

using System.Text.Json;
using System.Text.RegularExpressions;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static partial class ContentChunking
{
    // Approximate characters per token (conservative estimate)
    internal const long CharsPerToken = 4;

    // r'\n\s*\n'
    private static readonly Regex ParagraphPattern = new(@"\n" + PyRegex.Space + @"*\n", RegexOptions.CultureInvariant);

    // r'(?<=[.!?])\s+'
    private static readonly Regex SentencePattern = new(@"(?<=[.!?])" + PyRegex.Space + "+", RegexOptions.CultureInvariant);

    // r'^([A-Za-z_][A-Za-z0-9_\s]*):(.+?)(?=^[A-Za-z_][A-Za-z0-9_\s]*:|$)'
    private static readonly Regex SpeakerPattern = new(
        "^([A-Za-z_][A-Za-z0-9_" + PyRegex.SpaceClassBody + "]*):(.+?)(?=^[A-Za-z_][A-Za-z0-9_" + PyRegex.SpaceClassBody + "]*:|$)",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // r'(?=^[A-Za-z_][A-Za-z0-9_\s]*:)'
    private static readonly Regex SpeakerSplitPattern = new(
        "(?=^[A-Za-z_][A-Za-z0-9_" + PyRegex.SpaceClassBody + "]*:)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static ILogger Logger => PortServices.Logger(typeof(ContentChunking));

    /// <summary>
    /// Estimate token count using character-based heuristic.
    ///
    /// Uses ~4 characters per token as a conservative estimate.
    /// This is faster than actual tokenization and works across all LLM providers.
    /// </summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <returns>Estimated token count.</returns>
    internal static long EstimateTokens(string text)
    {
        return Py.FloorDiv(Py.Len(text), CharsPerToken);
    }

    /// <summary>Convert token count to approximate character count.</summary>
    internal static long TokensToChars(long tokens)
    {
        return tokens * CharsPerToken;
    }

    /// <summary>
    /// Determine whether content should be chunked based on size and entity density.
    ///
    /// Only chunks content that is both:
    /// 1. Large enough to potentially cause LLM issues (>= CHUNK_MIN_TOKENS)
    /// 2. High entity density (many entities per token)
    ///
    /// Short content processes fine regardless of density. This targets the specific
    /// failure case of large entity-dense inputs while preserving context for
    /// prose/narrative content and avoiding unnecessary chunking of small inputs.
    /// </summary>
    /// <param name="content">The content to evaluate.</param>
    /// <param name="episodeType">Type of episode (json, message, text).</param>
    /// <returns>True if content is large and has high entity density.</returns>
    internal static bool ShouldChunk(string content, EpisodeType episodeType)
    {
        var tokens = EstimateTokens(content);

        // Short content always processes fine - no need to chunk
        if (tokens < Helpers.ChunkMinTokens)
        {
            return false;
        }

        return EstimateHighDensity(content, episodeType, tokens);
    }

    /// <summary>
    /// Estimate whether content has high entity density.
    ///
    /// High-density content (many entities per token) benefits from chunking.
    /// Low-density content (prose, narratives) loses context when chunked.
    /// </summary>
    /// <param name="content">The content to analyze.</param>
    /// <param name="episodeType">Type of episode.</param>
    /// <param name="tokens">Pre-computed token count.</param>
    /// <returns>True if content appears to have high entity density.</returns>
    internal static bool EstimateHighDensity(string content, EpisodeType episodeType, long tokens)
    {
        if (episodeType == EpisodeType.Json)
        {
            return JsonLikelyDense(content, tokens);
        }
        else
        {
            return TextLikelyDense(content, tokens);
        }
    }

    /// <summary>
    /// Estimate entity density for JSON content.
    ///
    /// JSON is considered dense if it has many array elements or object keys,
    /// as each typically represents a distinct entity or data point.
    ///
    /// Heuristics:
    /// - Array: Count elements, estimate entities per 1000 tokens
    /// - Object: Count top-level keys
    /// </summary>
    /// <param name="content">JSON string content.</param>
    /// <param name="tokens">Token count.</param>
    /// <returns>True if JSON appears to have high entity density.</returns>
    internal static bool JsonLikelyDense(string content, long tokens)
    {
        object? data;
        try
        {
            data = PyJson.Loads(content);
        }
        catch (JsonException)
        {
            // Invalid JSON, fall back to text heuristics
            return TextLikelyDense(content, tokens);
        }

        if (data is List<object?> list)
        {
            // For arrays, each element likely contains entities
            long elementCount = list.Count;

            // Estimate density: elements per 1000 tokens
            var density = tokens > 0 ? Py.TrueDiv(elementCount, tokens) * 1000 : 0.0;
            return density > Helpers.ChunkDensityThreshold * 1000; // Scale threshold
        }
        else if (data is OrderedDictionary<string, object?> dict)
        {
            // For objects, count keys recursively (shallow)
            var keyCount = CountJsonKeys(dict, maxDepth: 2);
            var density = tokens > 0 ? Py.TrueDiv(keyCount, tokens) * 1000 : 0.0;
            return density > Helpers.ChunkDensityThreshold * 1000;
        }
        else
        {
            // Scalar value, no need to chunk
            return false;
        }
    }

    /// <summary>Count keys in a JSON object up to a certain depth.</summary>
    /// <param name="data">Dictionary to count keys in.</param>
    /// <param name="maxDepth">Maximum depth to traverse.</param>
    /// <param name="currentDepth">Current recursion depth.</param>
    /// <returns>Count of keys.</returns>
    internal static long CountJsonKeys(OrderedDictionary<string, object?> data, long maxDepth = 2, long currentDepth = 0)
    {
        if (currentDepth >= maxDepth)
        {
            return 0;
        }

        long count = data.Count;
        foreach (var value in data.Values)
        {
            if (value is OrderedDictionary<string, object?> nested)
            {
                count += CountJsonKeys(nested, maxDepth, currentDepth + 1);
            }
            else if (value is List<object?> items)
            {
                foreach (var item in items)
                {
                    if (item is OrderedDictionary<string, object?> nestedItem)
                    {
                        count += CountJsonKeys(nestedItem, maxDepth, currentDepth + 1);
                    }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Estimate entity density for text content.
    ///
    /// Uses capitalized words as a proxy for named entities (people, places,
    /// organizations, products). High ratio of capitalized words suggests
    /// high entity density.
    /// </summary>
    /// <param name="content">Text content.</param>
    /// <param name="tokens">Token count.</param>
    /// <returns>True if text appears to have high entity density.</returns>
    internal static bool TextLikelyDense(string content, long tokens)
    {
        if (tokens == 0)
        {
            return false;
        }

        // Split into words
        var words = PyText.Split(content);
        if (words.Count == 0)
        {
            return false;
        }

        // Count capitalized words (excluding sentence starters)
        // A word is "capitalized" if it starts with uppercase and isn't all caps
        long capitalizedCount = 0;
        foreach (var (i, word) in words.Index())
        {
            // Skip if it's likely a sentence starter (after . ! ? or first word)
            if (i == 0)
            {
                continue;
            }

            if (i > 0 && ".!?".Contains(Py.Slice(PyText.RStrip(words[i - 1]), -1, null), StringComparison.Ordinal))
            {
                continue;
            }

            // Check if capitalized (first char upper, not all caps)
            var cleaned = PyText.Strip(word, ".,!?;:'\"()[]{}");
            if (cleaned.Length > 0 && PyText.IsUpper(Py.At(cleaned, 0)) && !PyText.IsUpper(cleaned))
            {
                capitalizedCount += 1;
            }
        }

        // Calculate density: capitalized words per 1000 tokens
        var density = tokens > 0 ? Py.TrueDiv(capitalizedCount, tokens) * 1000 : 0.0;

        // Text density threshold is typically lower than JSON
        // A well-written article might have 5-10% named entities
        return density > Helpers.ChunkDensityThreshold * 500; // Half the JSON threshold
    }

    /// <summary>
    /// Split JSON content into chunks while preserving structure.
    ///
    /// For arrays: splits at element boundaries, keeping complete objects.
    /// For objects: splits at top-level key boundaries.
    /// </summary>
    /// <param name="content">JSON string to chunk.</param>
    /// <param name="chunkSizeTokens">Target size per chunk in tokens (default from env).</param>
    /// <param name="overlapTokens">Overlap between chunks in tokens (default from env).</param>
    /// <returns>List of JSON string chunks.</returns>
    internal static List<string> ChunkJsonContent(string content, long? chunkSizeTokens = null, long? overlapTokens = null)
    {
        // PORT: Python "x or default" also replaces 0, so the test covers null and 0.
        var chunkSizeTokensValue = chunkSizeTokens is null or 0 ? Helpers.ChunkTokenSize : chunkSizeTokens.Value;
        var overlapTokensValue = overlapTokens is null or 0 ? Helpers.ChunkOverlapTokens : overlapTokens.Value;

        var chunkSizeChars = TokensToChars(chunkSizeTokensValue);
        var overlapChars = TokensToChars(overlapTokensValue);

        object? data;
        try
        {
            data = PyJson.Loads(content);
        }
        catch (JsonException)
        {
            Log.FailedToParseJson(Logger);
            return ChunkTextContent(content, chunkSizeTokensValue, overlapTokensValue);
        }

        if (data is List<object?> list)
        {
            return ChunkJsonArray(list, chunkSizeChars, overlapChars);
        }
        else if (data is OrderedDictionary<string, object?> dict)
        {
            return ChunkJsonObject(dict, chunkSizeChars, overlapChars);
        }
        else
        {
            // Scalar value, return as-is
            return [content];
        }
    }

    /// <summary>Chunk a JSON array by splitting at element boundaries.</summary>
    internal static List<string> ChunkJsonArray(List<object?> data, long chunkSizeChars, long overlapChars)
    {
        if (data.Count == 0)
        {
            return ["[]"];
        }

        List<string> chunks = [];
        List<object?> currentElements = [];
        long currentSize = 2; // Account for '[]'

        foreach (var element in data)
        {
            var elementJson = PyJson.Dumps(element);
            var elementSize = Py.Len(elementJson) + 2; // Account for comma and space

            // Check if adding this element would exceed chunk size
            if (currentElements.Count > 0 && currentSize + elementSize > chunkSizeChars)
            {
                // Save current chunk
                chunks.Add(PyJson.Dumps(currentElements));

                // Start new chunk with overlap (include last few elements)
                var overlapElements = GetOverlapElements(currentElements, overlapChars);
                currentElements = overlapElements;
                currentSize = currentElements.Count > 0 ? Py.Len(PyJson.Dumps(currentElements)) : 2;
            }

            currentElements.Add(element);
            currentSize += elementSize;
        }

        // Don't forget the last chunk
        if (currentElements.Count > 0)
        {
            chunks.Add(PyJson.Dumps(currentElements));
        }

        return chunks.Count > 0 ? chunks : ["[]"];
    }

    /// <summary>Get elements from the end of a list that fit within overlap_chars.</summary>
    internal static List<object?> GetOverlapElements(List<object?> elements, long overlapChars)
    {
        if (elements.Count == 0)
        {
            return [];
        }

        List<object?> overlapElements = [];
        long currentSize = 2; // Account for '[]'

        for (var index = elements.Count - 1; index >= 0; index--)
        {
            var element = elements[index];
            var elementJson = PyJson.Dumps(element);
            var elementSize = Py.Len(elementJson) + 2;

            if (currentSize + elementSize > overlapChars)
            {
                break;
            }

            overlapElements.Insert(0, element);
            currentSize += elementSize;
        }

        return overlapElements;
    }

    /// <summary>Chunk a JSON object by splitting at top-level key boundaries.</summary>
    internal static List<string> ChunkJsonObject(OrderedDictionary<string, object?> data, long chunkSizeChars, long overlapChars)
    {
        if (data.Count == 0)
        {
            return ["{}"];
        }

        List<string> chunks = [];
        List<string> currentKeys = [];
        OrderedDictionary<string, object?> currentDict = [];
        long currentSize = 2; // Account for '{}'

        foreach (var (key, value) in data)
        {
            var entryJson = PyJson.Dumps(new OrderedDictionary<string, object?> { [key] = value });
            var entrySize = Py.Len(entryJson);

            // Check if adding this entry would exceed chunk size
            if (currentDict.Count > 0 && currentSize + entrySize > chunkSizeChars)
            {
                // Save current chunk
                chunks.Add(PyJson.Dumps(currentDict));

                // Start new chunk with overlap (include last few keys)
                var overlapDict = GetOverlapDict(currentDict, currentKeys, overlapChars);
                currentDict = overlapDict;
                currentKeys = [.. overlapDict.Keys];
                currentSize = currentDict.Count > 0 ? Py.Len(PyJson.Dumps(currentDict)) : 2;
            }

            currentDict[key] = value;
            currentKeys.Add(key);
            currentSize += entrySize;
        }

        // Don't forget the last chunk
        if (currentDict.Count > 0)
        {
            chunks.Add(PyJson.Dumps(currentDict));
        }

        return chunks.Count > 0 ? chunks : ["{}"];
    }

    /// <summary>Get key-value pairs from the end of a dict that fit within overlap_chars.</summary>
    internal static OrderedDictionary<string, object?> GetOverlapDict(OrderedDictionary<string, object?> data, List<string> keys, long overlapChars)
    {
        if (data.Count == 0 || keys.Count == 0)
        {
            return [];
        }

        OrderedDictionary<string, object?> overlapDict = [];
        long currentSize = 2; // Account for '{}'

        for (var index = keys.Count - 1; index >= 0; index--)
        {
            var key = keys[index];
            if (!data.ContainsKey(key))
            {
                continue;
            }

            var entryJson = PyJson.Dumps(new OrderedDictionary<string, object?> { [key] = data[key] });
            var entrySize = Py.Len(entryJson);

            if (currentSize + entrySize > overlapChars)
            {
                break;
            }

            overlapDict[key] = data[key];
            currentSize += entrySize;
        }

        // Reverse to maintain original order
        OrderedDictionary<string, object?> reversed = [];
        for (var index = overlapDict.Count - 1; index >= 0; index--)
        {
            var (key, value) = overlapDict.GetAt(index);
            reversed[key] = value;
        }

        return reversed;
    }

    /// <summary>
    /// Split text content at natural boundaries (paragraphs, sentences).
    ///
    /// Includes overlap to capture entities at chunk boundaries.
    /// </summary>
    /// <param name="content">Text to chunk.</param>
    /// <param name="chunkSizeTokens">Target size per chunk in tokens (default from env).</param>
    /// <param name="overlapTokens">Overlap between chunks in tokens (default from env).</param>
    /// <returns>List of text chunks.</returns>
    internal static List<string> ChunkTextContent(string content, long? chunkSizeTokens = null, long? overlapTokens = null)
    {
        // PORT: Python "x or default" also replaces 0, so the test covers null and 0.
        var chunkSizeTokensValue = chunkSizeTokens is null or 0 ? Helpers.ChunkTokenSize : chunkSizeTokens.Value;
        var overlapTokensValue = overlapTokens is null or 0 ? Helpers.ChunkOverlapTokens : overlapTokens.Value;

        var chunkSizeChars = TokensToChars(chunkSizeTokensValue);
        var overlapChars = TokensToChars(overlapTokensValue);

        if (Py.Len(content) <= chunkSizeChars)
        {
            return [content];
        }

        // Split into paragraphs first
        // PORT: the pattern has no group, so no item of the split is null.
        var paragraphs = PyRegex.Split(ParagraphPattern, content).OfType<string>().ToList();

        List<string> chunks = [];
        List<string> currentChunk = [];
        long currentSize = 0;

        foreach (var rawParagraph in paragraphs)
        {
            var paragraph = PyText.Strip(rawParagraph);
            if (paragraph.Length == 0)
            {
                continue;
            }

            var paraSize = Py.Len(paragraph);

            // If a single paragraph is too large, split it by sentences
            if (paraSize > chunkSizeChars)
            {
                // First, save current chunk if any
                if (currentChunk.Count > 0)
                {
                    chunks.Add(string.Join("\n\n", currentChunk));
                    currentChunk = [];
                    currentSize = 0;
                }

                // Split large paragraph by sentences
                var sentenceChunks = ChunkBySentences(paragraph, chunkSizeChars, overlapChars);
                chunks.AddRange(sentenceChunks);
                continue;
            }

            // Check if adding this paragraph would exceed chunk size
            if (currentChunk.Count > 0 && currentSize + paraSize + 2 > chunkSizeChars)
            {
                // Save current chunk
                chunks.Add(string.Join("\n\n", currentChunk));

                // Start new chunk with overlap
                var overlapText = GetOverlapText(string.Join("\n\n", currentChunk), overlapChars);
                if (overlapText.Length > 0)
                {
                    currentChunk = [overlapText];
                    currentSize = Py.Len(overlapText);
                }
                else
                {
                    currentChunk = [];
                    currentSize = 0;
                }
            }

            currentChunk.Add(paragraph);
            currentSize += paraSize + 2; // Account for '\n\n'
        }

        // Don't forget the last chunk
        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join("\n\n", currentChunk));
        }

        return chunks.Count > 0 ? chunks : [content];
    }

    /// <summary>Split text by sentence boundaries.</summary>
    internal static List<string> ChunkBySentences(string text, long chunkSizeChars, long overlapChars)
    {
        // Split on sentence-ending punctuation followed by whitespace
        // PORT: the pattern has no group, so no item of the split is null.
        var sentences = PyRegex.Split(SentencePattern, text).OfType<string>().ToList();

        List<string> chunks = [];
        List<string> currentChunk = [];
        long currentSize = 0;

        foreach (var rawSentence in sentences)
        {
            var sentence = PyText.Strip(rawSentence);
            if (sentence.Length == 0)
            {
                continue;
            }

            var sentSize = Py.Len(sentence);

            // If a single sentence is too large, split it by fixed size
            if (sentSize > chunkSizeChars)
            {
                if (currentChunk.Count > 0)
                {
                    chunks.Add(string.Join(" ", currentChunk));
                    currentChunk = [];
                    currentSize = 0;
                }

                // Split by fixed size as last resort
                var fixedChunks = ChunkBySize(sentence, chunkSizeChars, overlapChars);
                chunks.AddRange(fixedChunks);
                continue;
            }

            // Check if adding this sentence would exceed chunk size
            if (currentChunk.Count > 0 && currentSize + sentSize + 1 > chunkSizeChars)
            {
                chunks.Add(string.Join(" ", currentChunk));

                // Start new chunk with overlap
                var overlapText = GetOverlapText(string.Join(" ", currentChunk), overlapChars);
                if (overlapText.Length > 0)
                {
                    currentChunk = [overlapText];
                    currentSize = Py.Len(overlapText);
                }
                else
                {
                    currentChunk = [];
                    currentSize = 0;
                }
            }

            currentChunk.Add(sentence);
            currentSize += sentSize + 1;
        }

        if (currentChunk.Count > 0)
        {
            chunks.Add(string.Join(" ", currentChunk));
        }

        return chunks;
    }

    /// <summary>Split text by fixed character size (last resort).</summary>
    internal static List<string> ChunkBySize(string text, long chunkSizeChars, long overlapChars)
    {
        List<string> chunks = [];
        long start = 0;

        while (start < Py.Len(text))
        {
            var end = Math.Min(start + chunkSizeChars, Py.Len(text));

            // Try to break at word boundary
            if (end < Py.Len(text))
            {
                var spaceIdx = PyText.RFind(text, " ", start, end);
                if (spaceIdx > start)
                {
                    end = spaceIdx;
                }
            }

            chunks.Add(PyText.Strip(Py.Slice(text, start, end)));

            // Move start forward, ensuring progress even if overlap >= chunk_size
            // Always advance by at least (chunk_size - overlap) or 1 char minimum
            var minProgress = Math.Max(1, chunkSizeChars - overlapChars);
            start = Math.Max(start + minProgress, end - overlapChars);
        }

        return chunks;
    }

    /// <summary>Get the last overlap_chars characters of text, breaking at word boundary.</summary>
    internal static string GetOverlapText(string text, long overlapChars)
    {
        if (Py.Len(text) <= overlapChars)
        {
            return text;
        }

        var overlapStart = Py.Len(text) - overlapChars;

        // Find the next word boundary after overlap_start
        var spaceIdx = PyText.Find(text, " ", overlapStart);
        if (spaceIdx != -1)
        {
            return Py.Slice(text, spaceIdx + 1, null);
        }

        return Py.Slice(text, overlapStart, null);
    }

    /// <summary>
    /// Split conversation content preserving message boundaries.
    ///
    /// Never splits mid-message. Messages are identified by patterns like:
    /// - "Speaker: message"
    /// - JSON message arrays
    /// - Newline-separated messages
    /// </summary>
    /// <param name="content">Conversation content to chunk.</param>
    /// <param name="chunkSizeTokens">Target size per chunk in tokens (default from env).</param>
    /// <param name="overlapTokens">Overlap between chunks in tokens (default from env).</param>
    /// <returns>List of conversation chunks.</returns>
    internal static List<string> ChunkMessageContent(string content, long? chunkSizeTokens = null, long? overlapTokens = null)
    {
        // PORT: Python "x or default" also replaces 0, so the test covers null and 0.
        var chunkSizeTokensValue = chunkSizeTokens is null or 0 ? Helpers.ChunkTokenSize : chunkSizeTokens.Value;
        var overlapTokensValue = overlapTokens is null or 0 ? Helpers.ChunkOverlapTokens : overlapTokens.Value;

        var chunkSizeChars = TokensToChars(chunkSizeTokensValue);
        var overlapChars = TokensToChars(overlapTokensValue);

        if (Py.Len(content) <= chunkSizeChars)
        {
            return [content];
        }

        // Try to detect message format
        // Check if it's JSON (array of message objects)
        try
        {
            var data = PyJson.Loads(content);
            if (data is List<object?> list)
            {
                return ChunkMessageArray(list, chunkSizeChars, overlapChars);
            }
        }
        catch (JsonException)
        {
            // PORT: Python passes
        }

        // Try speaker pattern (e.g., "Alice: Hello")
        if (SpeakerPattern.IsMatch(content))
        {
            return ChunkSpeakerMessages(content, chunkSizeChars, overlapChars);
        }

        // Fallback to line-based chunking
        return ChunkByLines(content, chunkSizeChars, overlapChars);
    }

    /// <summary>Chunk a JSON array of message objects.</summary>
    internal static List<string> ChunkMessageArray(List<object?> messages, long chunkSizeChars, long overlapChars)
    {
        // Delegate to JSON array chunking
        var chunks = ChunkJsonArray(messages, chunkSizeChars, overlapChars);
        return chunks;
    }

    /// <summary>Chunk messages in 'Speaker: message' format.</summary>
    internal static List<string> ChunkSpeakerMessages(string content, long chunkSizeChars, long overlapChars)
    {
        // Split on speaker patterns
        // PORT: the pattern has no group, so no item of the split is null.
        var split = PyRegex.Split(SpeakerSplitPattern, content).OfType<string>();
        var messages = split.Where(m => PyText.Strip(m).Length > 0).Select(PyText.Strip).ToList();

        if (messages.Count == 0)
        {
            return [content];
        }

        List<string> chunks = [];
        List<string> currentMessages = [];
        long currentSize = 0;

        foreach (var message in messages)
        {
            var msgSize = Py.Len(message);

            // If a single message is too large, include it as its own chunk
            if (msgSize > chunkSizeChars)
            {
                if (currentMessages.Count > 0)
                {
                    chunks.Add(string.Join("\n", currentMessages));
                    currentMessages = [];
                    currentSize = 0;
                }

                chunks.Add(message);
                continue;
            }

            if (currentMessages.Count > 0 && currentSize + msgSize + 1 > chunkSizeChars)
            {
                chunks.Add(string.Join("\n", currentMessages));

                // Get overlap (last message(s) that fit)
                var overlapMessages = GetOverlapMessages(currentMessages, overlapChars);
                currentMessages = overlapMessages;
                currentSize = currentMessages.Sum(m => Py.Len(m)) + currentMessages.Count - 1;
            }

            currentMessages.Add(message);
            currentSize += msgSize + 1;
        }

        if (currentMessages.Count > 0)
        {
            chunks.Add(string.Join("\n", currentMessages));
        }

        return chunks.Count > 0 ? chunks : [content];
    }

    /// <summary>Get messages from the end that fit within overlap_chars.</summary>
    internal static List<string> GetOverlapMessages(List<string> messages, long overlapChars)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        List<string> overlap = [];
        long currentSize = 0;

        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var msg = messages[index];
            var msgSize = Py.Len(msg) + 1;
            if (currentSize + msgSize > overlapChars)
            {
                break;
            }

            overlap.Insert(0, msg);
            currentSize += msgSize;
        }

        return overlap;
    }

    /// <summary>Chunk content by line boundaries.</summary>
    internal static List<string> ChunkByLines(string content, long chunkSizeChars, long overlapChars)
    {
        var lines = PyText.Split(content, "\n");

        List<string> chunks = [];
        List<string> currentLines = [];
        long currentSize = 0;

        foreach (var line in lines)
        {
            var lineSize = Py.Len(line) + 1;

            if (currentLines.Count > 0 && currentSize + lineSize > chunkSizeChars)
            {
                chunks.Add(string.Join("\n", currentLines));

                // Get overlap lines
                var overlapText = string.Join("\n", currentLines);
                var overlap = GetOverlapText(overlapText, overlapChars);
                if (overlap.Length > 0)
                {
                    currentLines = PyText.Split(overlap, "\n");
                    currentSize = Py.Len(overlap);
                }
                else
                {
                    currentLines = [];
                    currentSize = 0;
                }
            }

            currentLines.Add(line);
            currentSize += lineSize;
        }

        if (currentLines.Count > 0)
        {
            chunks.Add(string.Join("\n", currentLines));
        }

        return chunks.Count > 0 ? chunks : [content];
    }

    // PORT: T = TypeVar('T') is the type parameter T of GenerateCoveringChunks.
    internal const long MaxCombinationsToEvaluate = 1000;

    /// <summary>Generate a random combination of k items from range(n).</summary>
    internal static IReadOnlyList<long> RandomCombination(long n, long k)
    {
        return [.. PortServices.Random.SampleRange(n, (int)k).Order()];
    }

    /// <summary>
    /// Generate chunks of items that cover all pairs using a greedy approach.
    ///
    /// Based on the Handshake Flights Problem / Covering Design problem.
    /// Each chunk of K items covers C(K,2) = K(K-1)/2 pairs. We greedily select
    /// chunks to maximize coverage of uncovered pairs, minimizing the total number
    /// of chunks needed to ensure every pair of items appears in at least one chunk.
    ///
    /// For large inputs where C(n,k) > MAX_COMBINATIONS_TO_EVALUATE, random sampling
    /// is used instead of exhaustive search to maintain performance.
    ///
    /// Lower bound (Schönheim): F >= ceil(N/K * ceil((N-1)/(K-1)))
    /// </summary>
    /// <param name="items">List of items to partition into covering chunks.</param>
    /// <param name="k">Maximum number of items per chunk.</param>
    /// <returns>
    /// List of tuples (chunk_items, global_indices) where global_indices maps
    /// each position in chunk_items to its index in the original items list.
    /// </returns>
    internal static List<(List<T>, List<long>)> GenerateCoveringChunks<T>(List<T> items, long k)
    {
        long n = items.Count;
        if (n <= k)
        {
            return [(items, [.. Enumerable.Range(0, (int)n).Select(i => (long)i)])];
        }

        // Track uncovered pairs using frozensets of indices
        // PORT: R14 gives a PySet of pairs, built in (i, j) order.
        PySet<PySet<long>> uncoveredPairs = [];
        for (long i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                uncoveredPairs.Add(new PySet<long>([i, j]) { Frozen = true });
            }
        }

        List<(List<T>, List<long>)> chunks = [];

        // Determine if we need to sample or can enumerate all combinations
        var totalCombinations = Py.Comb(n, k);
        var useSampling = totalCombinations > MaxCombinationsToEvaluate;

        while (uncoveredPairs.Count > 0)
        {
            // Greedy selection: find the chunk that covers the most uncovered pairs
            IReadOnlyList<long>? bestChunkIndices = null;
            long bestCoveredCount = 0;

            if (useSampling)
            {
                // Sample random combinations when there are too many to enumerate
                HashSet<PyTuple> seenCombinations = [];

                // Limit total attempts (including duplicates) to prevent infinite loops
                var maxTotalAttempts = MaxCombinationsToEvaluate * 3;
                long totalAttempts = 0;
                long samplesEvaluated = 0;
                while (samplesEvaluated < MaxCombinationsToEvaluate)
                {
                    totalAttempts += 1;
                    if (totalAttempts > maxTotalAttempts)
                    {
                        // Too many total attempts, break to avoid infinite loop
                        break;
                    }

                    var chunkIndices = RandomCombination(n, k);

                    // PORT: a tuple of ints is a PyTuple as the item of a set (R13).
                    var sample = PyTuple.From(chunkIndices.Select(index => (object?)index));
                    if (seenCombinations.Contains(sample))
                    {
                        continue;
                    }

                    seenCombinations.Add(sample);
                    samplesEvaluated += 1;

                    // Count how many uncovered pairs this chunk covers
                    var coveredCount = chunkIndices.Index()
                        .SelectMany(entry => Py.Slice(chunkIndices, entry.Index + 1, null).Select(idxJ => (idxI: entry.Item, idxJ)))
                        .LongCount(pair => uncoveredPairs.Contains(new PySet<long>([pair.idxI, pair.idxJ]) { Frozen = true }));

                    if (coveredCount > bestCoveredCount)
                    {
                        bestCoveredCount = coveredCount;
                        bestChunkIndices = chunkIndices;
                    }
                }
            }
            else
            {
                // Enumerate all combinations when feasible
                foreach (var chunkIndices in Py.Combinations(Enumerable.Range(0, (int)n).Select(i => (long)i).ToList(), (int)k))
                {
                    // Count how many uncovered pairs this chunk covers
                    var coveredCount = chunkIndices.Index()
                        .SelectMany(entry => Py.Slice(chunkIndices, entry.Index + 1, null).Select(idxJ => (idxI: entry.Item, idxJ)))
                        .LongCount(pair => uncoveredPairs.Contains(new PySet<long>([pair.idxI, pair.idxJ]) { Frozen = true }));

                    if (coveredCount > bestCoveredCount)
                    {
                        bestCoveredCount = coveredCount;
                        bestChunkIndices = chunkIndices;
                    }
                }
            }

            if (bestChunkIndices is null || bestCoveredCount == 0)
            {
                // Greedy search couldn't find a chunk covering uncovered pairs.
                // This can happen with random sampling. Fall back to creating
                // small chunks that directly cover remaining pairs.
                break;
            }

            // Mark pairs in this chunk as covered
            foreach (var (i, idxI) in bestChunkIndices.Index())
            {
                foreach (var idxJ in Py.Slice(bestChunkIndices, i + 1, null))
                {
                    uncoveredPairs.Discard(new PySet<long>([idxI, idxJ]) { Frozen = true });
                }
            }

            var chunkItems = bestChunkIndices.Select(idx => items[(int)idx]).ToList();
            chunks.Add((chunkItems, [.. bestChunkIndices]));
        }

        // Handle any remaining uncovered pairs that the greedy algorithm missed.
        // This can happen when random sampling fails to find covering chunks.
        // Create minimal chunks (size 2) to guarantee all pairs are covered.
        // PORT: D43. A set of ints has one fixed order in CPython; the port iterates in insertion order, so a call with k of 0 or 1 gives the same chunks in another order.
        foreach (var pair in uncoveredPairs)
        {
            var pairIndices = pair.Order().ToList();
            var chunkItems = pairIndices.Select(idx => items[(int)idx]).ToList();
            chunks.Add((chunkItems, pairIndices));
        }

        return chunks;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse JSON, falling back to text chunking")]
        internal static partial void FailedToParseJson(ILogger logger);
    }
}
