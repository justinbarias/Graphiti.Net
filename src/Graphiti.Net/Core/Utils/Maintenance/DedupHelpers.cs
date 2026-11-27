// Ported from graphiti_core/utils/maintenance/dedup_helpers.py
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

namespace Graphiti.Core.Utils.Maintenance;

using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Graphiti.PyCompat;

// PORT: each private name of this module is internal (R2): node_operations, bulk_utils, and ingestion.delta import
// them, and judge cases and the recording of the unit read them.
internal static partial class DedupHelpers
{
    internal const double NameEntropyThreshold = 1.5;

    internal const long MinNameLength = 6;

    internal const long MinTokenCount = 2;

    internal const double FuzzyJaccardThreshold = 0.9;

    internal const long MinhashPermutations = 32;

    internal const long MinhashBandSize = 4;

    internal const string EntityBaseLabel = "Entity";

    // PORT: a pattern inside a function becomes a static field (R13). Python writes the same pattern at two sites, so
    // each site has its own field (R1).
    // r'[\s]+' in _normalize_string_exact
    private static readonly Regex ExactWhitespacePattern = new("[" + PyRegex.SpaceClassBody + "]+", RegexOptions.CultureInvariant);

    // r"[^a-z0-9' ]". PORT: Python replaces one code point with one space, so a surrogate pair is one match (R13).
    private static readonly Regex FuzzyDisallowedPattern = new(@"(?:[\uD800-\uDBFF][\uDC00-\uDFFF]|[^a-z0-9' ])", RegexOptions.CultureInvariant);

    // r'[\s]+' in _normalize_name_for_fuzzy
    private static readonly Regex FuzzyWhitespacePattern = new("[" + PyRegex.SpaceClassBody + "]+", RegexOptions.CultureInvariant);

    /// <summary>Lowercase text and collapse whitespace so equal names map to the same key.</summary>
    internal static string NormalizeStringExact(string name)
    {
        var normalized = ExactWhitespacePattern.Replace(PyText.Lower(name), " ");
        return PyText.Strip(normalized);
    }

    /// <summary>Produce a fuzzier form that keeps alphanumerics and apostrophes for n-gram shingles.</summary>
    internal static string NormalizeNameForFuzzy(string name)
    {
        var normalized = FuzzyDisallowedPattern.Replace(NormalizeStringExact(name), " ");
        normalized = PyText.Strip(normalized);
        return FuzzyWhitespacePattern.Replace(normalized, " ");
    }

    /// <summary>
    /// Approximate text specificity using Shannon entropy over characters.
    ///
    /// We strip spaces, count how often each character appears, and sum
    /// probability * -log2(probability). Short or repetitive names yield low
    /// entropy, which signals we should defer resolution to the LLM instead of
    /// trusting fuzzy similarity.
    /// </summary>
    internal static double NameEntropy(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName))
        {
            return 0.0;
        }

        // PORT: the sum below runs in the order of the dict, so the dict keeps insertion order (R14).
        var counts = new OrderedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var @char in PyText.CodePoints(normalizedName.Replace(" ", string.Empty, StringComparison.Ordinal)))
        {
            counts[@char] = Py.Get(counts, @char, 0) + 1;
        }

        var total = counts.Values.Sum();
        if (total == 0)
        {
            return 0.0;
        }

        var entropy = 0.0;
        foreach (var count in counts.Values)
        {
            var probability = Py.TrueDiv(count, total);
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    /// <summary>Filter out very short or low-entropy names that are unreliable for fuzzy matching.</summary>
    internal static bool HasHighEntropy(string normalizedName)
    {
        var tokenCount = PyText.Split(normalizedName).Count;
        if (Py.Len(normalizedName) < MinNameLength && tokenCount < MinTokenCount)
        {
            return false;
        }

        return NameEntropy(normalizedName) >= NameEntropyThreshold;
    }

    // PORT: the set is only tested with `in`, intersected, and counted, so it is a HashSet (R14).

    /// <summary>Create 3-gram shingles from the normalized name for MinHash calculations.</summary>
    internal static HashSet<string> Shingles(string normalizedName)
    {
        var cleaned = normalizedName.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (Py.Len(cleaned) < 2)
        {
            return cleaned.Length > 0 ? new HashSet<string>(StringComparer.Ordinal) { cleaned } : new HashSet<string>(StringComparer.Ordinal);
        }

        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (long i = 0; i < Py.Len(cleaned) - 2; i++)
        {
            shingles.Add(Py.Slice(cleaned, i, i + 3));
        }

        return shingles;
    }

    // PORT: a MinHash value goes up to 2^64 - 1, so it is a ulong (R18, GAPS F15).

    /// <summary>Generate a deterministic 64-bit hash for a shingle given the permutation seed.</summary>
    internal static ulong HashShingle(string shingle, long seed)
    {
        var digest = PyHash.Blake2b(PyText.EncodeUtf8(Py.Str(seed) + ":" + shingle), 8);
        return BinaryPrimitives.ReadUInt64BigEndian(digest);
    }

    // PORT: `tuple[int, ...]` is IReadOnlyList (Part 3). `Iterable[str]` is IReadOnlyCollection, because the body
    // tests its truth (`if not shingles`) before it iterates it.

    /// <summary>Compute the MinHash signature for the shingle set across predefined permutations.</summary>
    internal static IReadOnlyList<ulong> MinhashSignature(IReadOnlyCollection<string> shingles)
    {
        if (shingles.Count == 0)
        {
            return [];
        }

        List<ulong> signature = [];
        for (long seed = 0; seed < MinhashPermutations; seed++)
        {
            var minHash = shingles.Min(shingle => HashShingle(shingle, seed));
            signature.Add(minHash);
        }

        return signature;
    }

    // PORT: each band is part of a dict key, so it is a PyTuple of ulong values (R14).

    /// <summary>Split the MinHash signature into fixed-size bands for locality-sensitive hashing.</summary>
    internal static List<PyTuple> LshBands(IEnumerable<ulong> signature)
    {
        var signatureList = signature.ToList();
        if (signatureList.Count == 0)
        {
            return [];
        }

        List<PyTuple> bands = [];
        for (long start = 0; start < signatureList.Count; start += MinhashBandSize)
        {
            var band = PyTuple.From(Py.Slice(signatureList, start, start + MinhashBandSize).Select(value => (object?)value));
            if (band.Count == MinhashBandSize)
            {
                bands.Add(band);
            }
        }

        return bands;
    }

    /// <summary>Return the Jaccard similarity between two shingle sets, handling empty edge cases.</summary>
    internal static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 1.0;
        }

        if (a.Count == 0 || b.Count == 0)
        {
            return 0.0;
        }

        var intersectionSet = new HashSet<string>(a, a.Comparer);
        intersectionSet.IntersectWith(b);
        var intersection = intersectionSet.Count;
        var unionSet = new HashSet<string>(a, a.Comparer);
        unionSet.UnionWith(b);
        var union = unionSet.Count;
        return union != 0 ? Py.TrueDiv(intersection, union) : 0.0;
    }

    // PORT: @lru_cache(maxsize=512) is a PyLruCache in a static field (R16). It is internal, because the recording test
    // reads its size (R2).
    internal static readonly PyLruCache<string, HashSet<string>> CachedShinglesCache = new(512);

    /// <summary>Cache shingle sets per normalized name to avoid recomputation within a worker.</summary>
    internal static HashSet<string> CachedShingles(string name)
    {
        return CachedShinglesCache.GetOrAdd(name, Shingles);
    }
}

// PORT: a @dataclass is a sealed record with { get; set; } properties (R7). `defaultdict(list)` is an
// OrderedDictionary with Py.SetDefault (R14). A key `tuple[int, tuple[int, ...]]` is a PyTuple (R14).

/// <summary>Precomputed lookup structures that drive entity deduplication heuristics.</summary>
internal sealed record DedupCandidateIndexes
{
    internal required List<EntityNode> ExistingNodes { get; set; }

    internal required OrderedDictionary<string, EntityNode> NodesByUuid { get; set; }

    internal required OrderedDictionary<string, List<EntityNode>> NormalizedExisting { get; set; }

    internal required OrderedDictionary<string, HashSet<string>> ShinglesByCandidate { get; set; }

    internal required OrderedDictionary<PyTuple, List<string>> LshBuckets { get; set; }
}

/// <summary>Mutable resolution bookkeeping shared across deterministic and LLM passes.</summary>
internal sealed record DedupResolutionState
{
    internal required List<EntityNode?> ResolvedNodes { get; set; }

    internal required OrderedDictionary<string, string> UuidMap { get; set; }

    internal required List<long> UnresolvedIndices { get; set; }

    internal List<(EntityNode, EntityNode)> DuplicatePairs { get; set; } = [];
}

internal static partial class DedupHelpers
{
    /// <summary>Merge useful extracted data into the canonical node and promote its type if needed.</summary>
    internal static EntityNode PromoteResolvedNode(EntityNode extractedNode, EntityNode resolvedNode)
    {
        MergeNonEmptyAttributes(resolvedNode, extractedNode);

        var resolvedSpecificLabels = resolvedNode.Labels.Where(label => label != "Entity").ToList();
        var extractedSpecificLabels = extractedNode.Labels.Where(label => label != "Entity").ToList();
        if (!new HashSet<string>(resolvedSpecificLabels, StringComparer.Ordinal).IsProperSubsetOf(new HashSet<string>(extractedSpecificLabels, StringComparer.Ordinal)))
        {
            return resolvedNode;
        }

        List<string> promotedLabels = [];
        foreach (var label in (List<string>)["Entity", .. resolvedSpecificLabels, .. extractedSpecificLabels])
        {
            if (!promotedLabels.Contains(label, StringComparer.Ordinal))
            {
                promotedLabels.Add(label);
            }
        }

        resolvedNode.Labels = promotedLabels;
        return resolvedNode;
    }

    /// <summary>Overlay extracted attributes without letting null/empty values erase existing data.</summary>
    internal static void MergeNonEmptyAttributes(EntityNode resolvedNode, EntityNode extractedNode)
    {
        if (extractedNode.Attributes.Count == 0)
        {
            return;
        }

        var merged = new OrderedDictionary<string, object?>(resolvedNode.Attributes);
        foreach (var (key, value) in extractedNode.Attributes)
        {
            if (value is null)
            {
                continue;
            }

            if (value is string text && PyText.Strip(text).Length == 0)
            {
                continue;
            }

            merged[key] = value;
        }

        resolvedNode.Attributes = merged;
    }

    internal static string? IdentityTypeForNode(
        EntityNode node,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        if (identityProperties is null || identityProperties.Count == 0)
        {
            return null;
        }

        foreach (var label in Enumerable.Reverse(node.Labels))
        {
            if (label != EntityBaseLabel && identityProperties.ContainsKey(label))
            {
                return label;
            }
        }

        return null;
    }

    // PORT: `value: object` is a value of open data, a node attribute (R6). A Python int is a long or a BigInteger.
    internal static object? NormalizeIdentityValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text)
        {
            var normalized = PyText.Strip(text);
            return normalized.Length > 0 ? normalized : null;
        }

        if (value is bool)
        {
            return value;
        }

        if (value is long or System.Numerics.BigInteger or double)
        {
            if (value is double number && double.IsNaN(number))
            {
                return null;
            }

            return value;
        }

        return null;
    }

    // PORT: a tuple that code compares with `==` and that holds a value of open data is a PyTuple (R6). The result
    // `tuple[str, tuple[tuple[str, object], ...]] | None` is a PyTuple of the identity type and a PyTuple of
    // (name, value) PyTuples, so that `==` has the value equality of Python (R6, R14).
    internal static PyTuple? IdentityKeyForNode(
        EntityNode node,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        var identityType = IdentityTypeForNode(node, identityProperties);
        if (identityType is null || identityProperties is null)
        {
            return null;
        }

        var propertyNames = identityProperties.TryGetValue(identityType, out var names) ? names : [];
        if (propertyNames.Count == 0)
        {
            return null;
        }

        var attributes = node.Attributes;
        List<PyTuple> values = [];
        foreach (var propertyName in propertyNames)
        {
            var value = NormalizeIdentityValue(attributes.TryGetValue(propertyName, out var raw) ? raw : null);
            if (value is null)
            {
                return null;
            }

            values.Add(new PyTuple(propertyName, value));
        }

        return new PyTuple(identityType, PyTuple.From(values));
    }

    /// <summary>Resolve nodes whose configured identity properties exactly match a candidate.</summary>
    internal static void ResolveWithIdentityProperties(
        List<EntityNode> extractedNodes,
        DedupCandidateIndexes indexes,
        DedupResolutionState state,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        if (identityProperties is null || identityProperties.Count == 0)
        {
            return;
        }

        foreach (var (idx, node) in extractedNodes.Index())
        {
            if (state.ResolvedNodes[idx] is not null)
            {
                continue;
            }

            var identityKey = IdentityKeyForNode(node, identityProperties);
            if (identityKey is null)
            {
                continue;
            }

            var matches = indexes.ExistingNodes
                .Where(candidate => candidate.Uuid != node.Uuid
                    && identityKey.Equals(IdentityKeyForNode(candidate, identityProperties)))
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            Py.SortInPlace(matches, candidate => new PyTuple(candidate.CreatedAt, candidate.Uuid));
            var match = PromoteResolvedNode(node, matches[0]);
            state.ResolvedNodes[idx] = match;
            state.UuidMap[node.Uuid] = match.Uuid;
            state.DuplicatePairs.Add((node, match));
        }
    }

    /// <summary>Precompute exact and fuzzy lookup structures once per dedupe run.</summary>
    internal static DedupCandidateIndexes BuildCandidateIndexes(List<EntityNode> existingNodes)
    {
        var normalizedExisting = new OrderedDictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        var nodesByUuid = new OrderedDictionary<string, EntityNode>(StringComparer.Ordinal);
        var shinglesByCandidate = new OrderedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var lshBuckets = new OrderedDictionary<PyTuple, List<string>>();

        foreach (var candidate in existingNodes)
        {
            var normalized = NormalizeStringExact(candidate.Name);
            Py.SetDefault(normalizedExisting, normalized, []).Add(candidate);
            nodesByUuid[candidate.Uuid] = candidate;

            var shingles = CachedShingles(NormalizeNameForFuzzy(candidate.Name));
            shinglesByCandidate[candidate.Uuid] = shingles;

            var signature = MinhashSignature(shingles);
            foreach (var (bandIndex, band) in LshBands(signature).Index())
            {
                Py.SetDefault(lshBuckets, new PyTuple((long)bandIndex, band), []).Add(candidate.Uuid);
            }
        }

        return new DedupCandidateIndexes
        {
            ExistingNodes = existingNodes,
            NodesByUuid = nodesByUuid,
            NormalizedExisting = normalizedExisting,
            ShinglesByCandidate = shinglesByCandidate,
            LshBuckets = lshBuckets,
        };
    }

    // PORT: `candidate_ids` is iterated with no sort, so it is a PySet<string> in insertion order (R14).

    /// <summary>
    /// Attempt deterministic resolution using exact name hits and fuzzy MinHash comparisons.
    ///
    /// Exact normalized-name matching runs first for *all* names regardless of
    /// length or entropy.  The entropy gate only guards the fuzzy (MinHash/LSH)
    /// path where short or low-entropy names produce unreliable shingle sets.
    /// </summary>
    internal static void ResolveWithSimilarity(
        List<EntityNode> extractedNodes,
        DedupCandidateIndexes indexes,
        DedupResolutionState state)
    {
        foreach (var (idx, node) in extractedNodes.Index())
        {
            var normalizedExact = NormalizeStringExact(node.Name);
            var normalizedFuzzy = NormalizeNameForFuzzy(node.Name);

            // --- exact-name matching (always attempted) ---
            var existingMatches = indexes.NormalizedExisting.TryGetValue(normalizedExact, out var found) ? found : [];
            if (existingMatches.Count == 1)
            {
                var match = PromoteResolvedNode(node, existingMatches[0]);
                state.ResolvedNodes[idx] = match;
                state.UuidMap[node.Uuid] = match.Uuid;
                if (match.Uuid != node.Uuid)
                {
                    state.DuplicatePairs.Add((node, match));
                }

                continue;
            }

            if (existingMatches.Count > 1)
            {
                // Ambiguous: multiple candidates share the same normalized name.
                // Escalate to LLM so it can pick the best match.
                state.UnresolvedIndices.Add(idx);
                continue;
            }

            // --- entropy gate (protects fuzzy matching only) ---
            if (!HasHighEntropy(normalizedFuzzy))
            {
                state.UnresolvedIndices.Add(idx);
                continue;
            }

            // --- fuzzy matching via MinHash/LSH ---
            var shingles = CachedShingles(normalizedFuzzy);
            var signature = MinhashSignature(shingles);
            var candidateIds = new PySet<string>(comparer: StringComparer.Ordinal);
            foreach (var (bandIndex, band) in LshBands(signature).Index())
            {
                foreach (var candidateId in indexes.LshBuckets.TryGetValue(new PyTuple((long)bandIndex, band), out var bucket) ? bucket : [])
                {
                    candidateIds.Add(candidateId);
                }
            }

            EntityNode? bestCandidate = null;
            var bestScore = 0.0;
            foreach (var candidateId in candidateIds)
            {
                var candidateShingles = indexes.ShinglesByCandidate.TryGetValue(candidateId, out var stored) ? stored : new HashSet<string>(StringComparer.Ordinal);
                var score = JaccardSimilarity(shingles, candidateShingles);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = indexes.NodesByUuid.TryGetValue(candidateId, out var candidate) ? candidate : null;
                }
            }

            if (bestCandidate is not null && bestScore >= FuzzyJaccardThreshold)
            {
                bestCandidate = PromoteResolvedNode(node, bestCandidate);
                state.ResolvedNodes[idx] = bestCandidate;
                state.UuidMap[node.Uuid] = bestCandidate.Uuid;
                if (bestCandidate.Uuid != node.Uuid)
                {
                    state.DuplicatePairs.Add((node, bestCandidate));
                }

                continue;
            }

            state.UnresolvedIndices.Add(idx);
        }
    }

    // PORT: `__all__` only lists names, so it has no member (R1).
}
