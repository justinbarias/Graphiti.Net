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
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.PyCompat;

// PORT: the pilot units search.search_filters and utils.content_chunking ported some members of this module for real
// (RULES.md R24): SAFE_CYPHER_IDENTIFIER_PATTERN, the CHUNK_* constants, and validate_node_labels. load_dotenv() is
// not ported (R17).
internal static class Helpers
{
    // PORT: internal, not private (R13), because nodes.py and driver/relate/operations/graph_ops.py import it.
    // Python: re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
    internal static readonly Regex SafeCypherIdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    internal static readonly bool UseParallelRuntime = PyEnv.Truthy("USE_PARALLEL_RUNTIME");

    // Default cap on the number of coroutines that can run concurrently inside
    // `semaphore_gather`. This is a *fallback* default — callers should pass
    // `max_coroutines` explicitly (typically sourced from
    // `Graphiti(max_coroutines=...)` or `GraphitiClients.max_coroutines`) to override
    // it on a per-instance basis. The `SEMAPHORE_LIMIT` environment variable is
    // still respected for backwards compatibility, but is no longer the primary way
    // to configure concurrency.
    internal const long DefaultSemaphoreLimit = 20;

    internal static readonly long SemaphoreLimit = PyEnv.Int("SEMAPHORE_LIMIT", DefaultSemaphoreLimit);

    internal const long DefaultPageLimit = 20;

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

    // PORT: the parameter is `neo4j_time.DateTime | str | None`. The Neo4j project converts a driver date to PyDateTime
    // before the value enters the core (R19), so the union is PyDateTime, string, or null, which is object? (Part 3).
    // TODO(port): parse_db_date
    internal static PyDateTime? ParseDbDate(object? inputDate)
    {
        throw new NotSupportedException("TODO(port): parse_db_date");
    }

    // TODO(port): get_default_group_id
    internal static string GetDefaultGroupId(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_default_group_id");
    }

    // TODO(port): lucene_sanitize
    internal static string LuceneSanitize(string query)
    {
        throw new NotSupportedException("TODO(port): lucene_sanitize");
    }

    // TODO(port): no rule for the numpy result type NDArray. The function returns a one-dimensional float array, so it
    // is List<double>, as the parameter is (R18).
    // TODO(port): normalize_l2
    internal static List<double> NormalizeL2(List<double> embedding)
    {
        throw new NotSupportedException("TODO(port): normalize_l2");
    }

    // Use this instead of asyncio.gather() to bound coroutines
    // PORT: R8. Each call is a lambda, and the overloads follow Asyncio.SemaphoreGatherAsync. A maxCoroutines of null or
    // 0 means SemaphoreLimit.
    // TODO(port): semaphore_gather
    internal static Task<List<T>> SemaphoreGatherAsync<T>(
        IReadOnlyList<Func<Task<T>>> coroutines,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): semaphore_gather");
    }

    // PORT: semaphore_gather for calls that return None. Python returns a list of None, which no caller reads.
    // TODO(port): semaphore_gather
    internal static Task SemaphoreGatherAsync(
        IReadOnlyList<Func<Task>> coroutines,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): semaphore_gather");
    }

    // PORT: the one call with different result types, search/search.py:174, passes four coroutines and unpacks a
    // tuple of four (R8).
    // TODO(port): semaphore_gather
    internal static Task<(T1, T2, T3, T4)> SemaphoreGatherAsync<T1, T2, T3, T4>(
        Func<Task<T1>> first,
        Func<Task<T2>> second,
        Func<Task<T3>> third,
        Func<Task<T4>> fourth,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): semaphore_gather");
    }

    // TODO(port): validate_group_id
    internal static bool ValidateGroupId(string? groupId)
    {
        throw new NotSupportedException("TODO(port): validate_group_id");
    }

    // TODO(port): validate_group_ids
    internal static bool ValidateGroupIds(List<string>? groupIds)
    {
        throw new NotSupportedException("TODO(port): validate_group_ids");
    }

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

    // PORT: a custom type is a GraphTypeDescriptor (R15, decision D26).
    // TODO(port): validate_excluded_entity_types
    internal static bool ValidateExcludedEntityTypes(
        List<string>? excludedEntityTypes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null)
    {
        throw new NotSupportedException("TODO(port): validate_excluded_entity_types");
    }
}
