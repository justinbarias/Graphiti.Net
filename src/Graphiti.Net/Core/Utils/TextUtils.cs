// Ported from graphiti_core/utils/text_utils.py
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

using System.Text.RegularExpressions;
using Graphiti.PyCompat;

internal static class TextUtils
{
    // Python: r'[.!?](?:\s|$)'. Look for sentence boundaries: period, exclamation, or question mark followed by space or end.
    private static readonly Regex SentencePattern = new(@"[.!?](?:" + PyRegex.Space + "|$)", RegexOptions.CultureInvariant);

    // Maximum length for entity/community summaries
    internal const long MaxSummaryChars = 1000;

    // Saga / thread summaries persist at most 4000 characters as a safety cap.
    internal const long SagaSummaryMaxChars = 4000;

    /// <summary>Truncate text to at most max_bytes without splitting a UTF-8 sequence.</summary>
    internal static string TruncateUtf8ToBytes(string text, long maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        var encoded = PyText.EncodeUtf8(text);
        if (encoded.Length <= maxBytes)
        {
            return text;
        }

        return PyText.DecodeUtf8(encoded.AsSpan(0, (int)maxBytes), "ignore");
    }

    /// <summary>
    /// Truncate text at or about max_chars while respecting sentence boundaries.
    ///
    /// Attempts to truncate at the last complete sentence before max_chars.
    /// If no sentence boundary is found before max_chars, truncates at max_chars.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxChars">Maximum number of characters.</param>
    /// <returns>Truncated text.</returns>
    internal static string TruncateAtSentence(string text, long maxChars)
    {
        if (string.IsNullOrEmpty(text) || Py.Len(text) <= maxChars)
        {
            return text;
        }

        // Find all sentence boundaries (., !, ?) up to max_chars
        var truncated = Py.Slice(text, null, maxChars);

        // Look for sentence boundaries: period, exclamation, or question mark followed by space or end
        var matches = SentencePattern.Matches(truncated);
        if (matches.Count > 0)
        {
            // Truncate at the last sentence boundary found
            var lastMatch = matches[^1];

            // PORT: a match position is a UTF-16 index, and Python slices by code point (R13).
            var end = Py.Len(truncated[..(lastMatch.Index + lastMatch.Length)]);
            return PyText.RStrip(Py.Slice(text, null, end));
        }

        // No sentence boundary found, truncate at max_chars
        return PyText.RStrip(truncated);
    }

    /// <summary>
    /// Concatenate episode contents with enumerated headers.
    ///
    /// When given a single episode, returns its content as-is.
    /// When given multiple episodes, each is prefixed with an <c>[Episode N]</c>
    /// header so the LLM can distinguish where one ends and the next begins.
    /// </summary>
    internal static string ConcatenateEpisodes(List<EpisodicNode> episodes)
    {
        if (episodes.Count == 1)
        {
            return episodes[0].Content;
        }

        List<string> parts = [];
        for (var i = 0; i < episodes.Count; i++)
        {
            var ep = episodes[i];

            // PORT: `if ep.valid_at else 'unknown'`. EpisodicNode.valid_at is a required datetime (nodes.py), and Node has
            // validate_assignment=True, so a valid model never holds None there and the 'unknown' branch cannot run. It is
            // left out, because EpisodicNode.ValidAt is a PyDateTime that cannot be null, and the branch needs a `!` or a warning.
            var timestamp = ep.ValidAt.IsoFormat();
            parts.Add($"[Episode {i}] (timestamp: {timestamp})\n{ep.Content}");
        }

        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Render <c>timeline</c> in order, numbering the members of <c>episodes</c> by their index in
    /// <c>episodes</c> and marking every other episode as context only.
    /// </summary>
    internal static string ConcatenateTimeline(List<EpisodicNode> episodes, List<EpisodicNode> timeline)
    {
        // PORT: a dict comprehension, so a later episode with the same uuid takes the index.
        Dictionary<string, long> indexByUuid = new(StringComparer.Ordinal);
        for (var i = 0; i < episodes.Count; i++)
        {
            indexByUuid[episodes[i].Uuid] = i;
        }

        List<string> parts = [];
        foreach (var ep in timeline)
        {
            // PORT: `if ep.valid_at else 'unknown'` is left out, as in concatenate_episodes.
            var timestamp = ep.ValidAt.IsoFormat();
            string header;
            if (!indexByUuid.TryGetValue(ep.Uuid, out var idx))
            {
                header = $"[CONTEXT EPISODE] (timestamp: {timestamp})";
            }
            else
            {
                header = $"[Episode {idx}] (timestamp: {timestamp})";
            }

            parts.Add($"{header}\n{ep.Content}");
        }

        return string.Join("\n\n", parts);
    }
}
