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

using System.Text;
using System.Text.RegularExpressions;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.PyCompat;

// PORT: load_dotenv() is not ported (R17).
internal static class Helpers
{
    // PORT: internal, not private (R13), because nodes.py and driver/relate/operations/graph_ops.py import it.
    // Python: re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
    internal static readonly Regex SafeCypherIdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    // Python: re.match(r'^[a-zA-Z0-9_-]+$', group_id), inside validate_group_id. A pattern inside a function is a static field (R13).
    private static readonly Regex GroupIdPattern = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant);

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
    // The branch `isinstance(input_date, neo4j_time.DateTime): return input_date.to_native()` is that conversion. A
    // PyDateTime then takes the last branch, which returns the input as it is. A value of another type cannot be the
    // result type, so Py.AsDateTime throws InvalidCastException for it (open item O4 holds the int case).
    internal static PyDateTime? ParseDbDate(object? inputDate)
    {
        if (inputDate is string text)
        {
            return PyDateTime.FromIsoFormat(text);
        }

        return inputDate is null ? null : Py.AsDateTime(inputDate);
    }

    /// <summary>
    /// This function differentiates the default group id based on the database type.
    /// For most databases, the default group id is an empty string, while there are database types that require a specific default group id.
    /// </summary>
    internal static string GetDefaultGroupId(GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            return "_";
        }
        else
        {
            return string.Empty;
        }
    }

    internal static string LuceneSanitize(string query)
    {
        // Escape special characters from a query before passing into Lucene
        // + - && || ! ( ) { } [ ] ^ " ~ * ? : \ /
        // PORT: str.maketrans with translate is a loop over the code points with a switch, in the order of the Python map (R12).
        var sanitized = new StringBuilder();
        foreach (var ch in PyText.CodePoints(query))
        {
            sanitized.Append(ch switch
            {
                "+" => @"\+",
                "-" => @"\-",
                "&" => @"\&",
                "|" => @"\|",
                "!" => @"\!",
                "(" => @"\(",
                ")" => @"\)",
                "{" => @"\{",
                "}" => @"\}",
                "[" => @"\[",
                "]" => @"\]",
                "^" => @"\^",
                "\"" => "\\\"",
                "~" => @"\~",
                "*" => @"\*",
                "?" => @"\?",
                ":" => @"\:",
                "\\" => @"\\",
                "/" => @"\/",
                "O" => @"\O",
                "R" => @"\R",
                "N" => @"\N",
                "T" => @"\T",
                "A" => @"\A",
                "D" => @"\D",
                _ => ch,
            });
        }

        return sanitized.ToString();
    }

    // PORT: the numpy result type NDArray is List<double>, as the parameter is (R20).
    internal static List<double> NormalizeL2(List<double> embedding)
    {
        // PORT: numpy sums the squares in pairs, so a plain loop in index order differs in the last bits (R18).
        var norm = Math.Sqrt(PyNumpy.AddReduce([.. embedding.Select(x => x * x)]));
        if (norm == 0)
        {
            return embedding;
        }

        return [.. embedding.Select(x => x / norm)];
    }

    // Use this instead of asyncio.gather() to bound coroutines
    // PORT: R8. Each call is a lambda, and the overloads follow Asyncio.SemaphoreGatherAsync. `max_coroutines or
    // SEMAPHORE_LIMIT`: a maxCoroutines of null or 0 means SemaphoreLimit.
    internal static Task<List<T>> SemaphoreGatherAsync<T>(
        IReadOnlyList<Func<Task<T>>> coroutines,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default) =>
        Asyncio.SemaphoreGatherAsync(coroutines, maxCoroutines is null or 0 ? SemaphoreLimit : maxCoroutines.Value, cancellationToken);

    // PORT: semaphore_gather for calls that return None. Python returns a list of None, which no caller reads.
    internal static Task SemaphoreGatherAsync(
        IReadOnlyList<Func<Task>> coroutines,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default) =>
        Asyncio.SemaphoreGatherAsync(coroutines, maxCoroutines is null or 0 ? SemaphoreLimit : maxCoroutines.Value, cancellationToken);

    // PORT: the one call with different result types, search/search.py:174, passes four coroutines and unpacks a
    // tuple of four (R8).
    internal static Task<(T1, T2, T3, T4)> SemaphoreGatherAsync<T1, T2, T3, T4>(
        Func<Task<T1>> first,
        Func<Task<T2>> second,
        Func<Task<T3>> third,
        Func<Task<T4>> fourth,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default) =>
        Asyncio.SemaphoreGatherAsync(first, second, third, fourth, maxCoroutines is null or 0 ? SemaphoreLimit : maxCoroutines.Value, cancellationToken);

    /// <summary>
    /// Validate that a group_id contains only ASCII alphanumeric characters, dashes, and underscores.
    /// </summary>
    /// <param name="groupId">The group_id to validate.</param>
    /// <returns>True if valid, False otherwise.</returns>
    /// <exception cref="GroupIdValidationException">If group_id contains invalid characters.</exception>
    internal static bool ValidateGroupId(string? groupId)
    {
        // Allow empty string (default case)
        if (string.IsNullOrEmpty(groupId))
        {
            return true;
        }

        // Check if string contains only ASCII alphanumeric characters, dashes, or underscores
        // Pattern matches: letters (a-z, A-Z), digits (0-9), hyphens (-), and underscores (_)
        if (!GroupIdPattern.IsMatch(groupId))
        {
            throw new GroupIdValidationException(groupId);
        }

        return true;
    }

    /// <summary>Validate a list of group ids used by search paths.</summary>
    internal static bool ValidateGroupIds(List<string>? groupIds)
    {
        if (groupIds is null)
        {
            return true;
        }

        foreach (var groupId in groupIds)
        {
            ValidateGroupId(groupId);
        }

        return true;
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

    /// <summary>
    /// Validate that excluded entity types are valid type names.
    /// </summary>
    /// <param name="excludedEntityTypes">List of entity type names to exclude.</param>
    /// <param name="entityTypes">Dictionary of available custom entity types.</param>
    /// <returns>True if valid.</returns>
    /// <exception cref="ArgumentException">If any excluded type names are invalid (a Python ValueError).</exception>
    // PORT: a custom type is a GraphTypeDescriptor (R15, decision D26).
    internal static bool ValidateExcludedEntityTypes(
        List<string>? excludedEntityTypes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null)
    {
        if (excludedEntityTypes is null || excludedEntityTypes.Count == 0)
        {
            return true;
        }

        // Build set of available type names
        HashSet<string> availableTypes = new(StringComparer.Ordinal) { "Entity" }; // Default type is always available
        if (entityTypes is not null && entityTypes.Count > 0)
        {
            availableTypes.UnionWith(entityTypes.Keys);
        }

        // Check for invalid type names
        HashSet<string> invalidTypes = new(excludedEntityTypes, StringComparer.Ordinal);
        invalidTypes.ExceptWith(availableTypes);
        if (invalidTypes.Count > 0)
        {
            // PORT: sorted() orders by code point (R12), and the f-string shows each list with repr (R11).
            List<object?> invalidSorted = [.. invalidTypes.OrderBy(t => t, Py.StringComparer)];
            List<object?> availableSorted = [.. availableTypes.OrderBy(t => t, Py.StringComparer)];
            throw new ArgumentException(
                $"Invalid excluded entity types: {Py.Repr(invalidSorted)}. Available types: {Py.Repr(availableSorted)}");
        }

        return true;
    }
}
