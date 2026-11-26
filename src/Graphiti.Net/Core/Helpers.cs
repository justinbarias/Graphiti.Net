// Ported from graphiti_core/helpers.py
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

namespace Graphiti.Core;

using System.Text.RegularExpressions;
using Graphiti.PyCompat;

// PORT: the pilot units search.search_filters and utils.content_chunking port only some members of this module
// (RULES.md R24): SAFE_CYPHER_IDENTIFIER_PATTERN, the CHUNK_* constants, and validate_node_labels.
internal static class Helpers
{
    // PORT: internal, not private (R13), because nodes.py and driver/relate/operations/graph_ops.py import it.
    // Python: re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
    internal static readonly Regex SafeCypherIdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    // Content chunking configuration for entity extraction
    // Density-based chunking: only chunk high-density content (many entities per token)
    // This targets the failure case (large entity-dense inputs) while preserving
    // context for prose/narrative content
    internal static readonly long ChunkTokenSize = PyEnv.Int("CHUNK_TOKEN_SIZE", 3000);

    internal static readonly long ChunkOverlapTokens = PyEnv.Int("CHUNK_OVERLAP_TOKENS", 200);

    // Minimum tokens before considering chunking - short content processes fine regardless of density
    // PORT: PyPatch, because tests/utils/test_content_chunking.py replaces it (R17).
    internal static readonly PyPatch<long> ChunkMinTokens = new(PyEnv.Int("CHUNK_MIN_TOKENS", 1000));

    // Entity density threshold: chunk if estimated density > this value
    // For JSON: elements per 1000 tokens > threshold * 1000 (e.g., 0.15 = 150 elements/1000 tokens)
    // For Text: capitalized words per 1000 tokens > threshold * 500 (e.g., 0.15 = 75 caps/1000 tokens)
    // Higher values = more conservative (less chunking), targets P95+ density cases
    // Examples that trigger chunking at 0.15: AWS cost data (12mo), bulk data imports, entity-dense JSON
    // Examples that DON'T chunk at 0.15: meeting transcripts, news articles, documentation
    // PORT: PyPatch, because tests/utils/test_content_chunking.py replaces it (R17).
    internal static readonly PyPatch<double> ChunkDensityThreshold = new(PyEnv.Float("CHUNK_DENSITY_THRESHOLD", 0.15));

    /// <summary>Validate that node labels are safe to interpolate into Cypher label expressions.</summary>
    internal static bool ValidateNodeLabels(List<string>? nodeLabels)
    {
        if (nodeLabels is null || nodeLabels.Count == 0)
        {
            return true;
        }

        var invalidLabels = nodeLabels.Where(label => !SafeCypherIdentifierPattern.IsMatch(label)).ToList();
        if (invalidLabels.Count > 0)
        {
            throw new NodeLabelValidationException(invalidLabels);
        }

        return true;
    }
}
