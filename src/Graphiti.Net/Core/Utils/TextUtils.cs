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

internal static class TextUtils
{
    // Maximum length for entity/community summaries
    internal const long MaxSummaryChars = 1000;

    // Saga / thread summaries persist at most 4000 characters as a safety cap.
    internal const long SagaSummaryMaxChars = 4000;

    // TODO(port): truncate_utf8_to_bytes
    internal static string TruncateUtf8ToBytes(string text, long maxBytes)
    {
        throw new NotSupportedException("TODO(port): truncate_utf8_to_bytes");
    }

    // TODO(port): truncate_at_sentence
    internal static string TruncateAtSentence(string text, long maxChars)
    {
        throw new NotSupportedException("TODO(port): truncate_at_sentence");
    }

    // TODO(port): concatenate_episodes
    internal static string ConcatenateEpisodes(List<EpisodicNode> episodes)
    {
        throw new NotSupportedException("TODO(port): concatenate_episodes");
    }

    // TODO(port): concatenate_timeline
    internal static string ConcatenateTimeline(List<EpisodicNode> episodes, List<EpisodicNode> timeline)
    {
        throw new NotSupportedException("TODO(port): concatenate_timeline");
    }
}
