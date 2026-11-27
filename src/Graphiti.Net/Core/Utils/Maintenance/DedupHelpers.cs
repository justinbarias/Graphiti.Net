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

    // TODO(port): _normalize_string_exact
    internal static string NormalizeStringExact(string name)
    {
        throw new NotSupportedException("TODO(port): _normalize_string_exact");
    }

    // TODO(port): _normalize_name_for_fuzzy
    internal static string NormalizeNameForFuzzy(string name)
    {
        throw new NotSupportedException("TODO(port): _normalize_name_for_fuzzy");
    }

    // TODO(port): _name_entropy
    internal static double NameEntropy(string normalizedName)
    {
        throw new NotSupportedException("TODO(port): _name_entropy");
    }

    // TODO(port): _has_high_entropy
    internal static bool HasHighEntropy(string normalizedName)
    {
        throw new NotSupportedException("TODO(port): _has_high_entropy");
    }

    // PORT: the set is only tested with `in`, intersected, and counted, so it is a HashSet (R14).
    // TODO(port): _shingles
    internal static HashSet<string> Shingles(string normalizedName)
    {
        throw new NotSupportedException("TODO(port): _shingles");
    }

    // PORT: a MinHash value goes up to 2^64 - 1, so it is a ulong (R18, GAPS F15).
    // TODO(port): _hash_shingle
    internal static ulong HashShingle(string shingle, long seed)
    {
        throw new NotSupportedException("TODO(port): _hash_shingle");
    }

    // PORT: `tuple[int, ...]` is IReadOnlyList (Part 3). `Iterable[str]` is IReadOnlyCollection, because the body
    // tests its truth (`if not shingles`) before it iterates it.
    // TODO(port): _minhash_signature
    internal static IReadOnlyList<ulong> MinhashSignature(IReadOnlyCollection<string> shingles)
    {
        throw new NotSupportedException("TODO(port): _minhash_signature");
    }

    // PORT: each band is part of a dict key, so it is a PyTuple of ulong values (R14).
    // TODO(port): _lsh_bands
    internal static List<PyTuple> LshBands(IEnumerable<ulong> signature)
    {
        throw new NotSupportedException("TODO(port): _lsh_bands");
    }

    // TODO(port): _jaccard_similarity
    internal static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
    {
        throw new NotSupportedException("TODO(port): _jaccard_similarity");
    }

    // PORT: @lru_cache(maxsize=512) is a PyLruCache<string, HashSet<string>> in a static field (R16). The unit adds the
    // field with the body.
    // TODO(port): _cached_shingles
    internal static HashSet<string> CachedShingles(string name)
    {
        throw new NotSupportedException("TODO(port): _cached_shingles");
    }
}

// PORT: a @dataclass is a sealed record with { get; set; } properties (R7). `defaultdict(list)` is an
// OrderedDictionary with Py.SetDefault (R14). A key `tuple[int, tuple[int, ...]]` is a PyTuple (R14).
internal sealed record DedupCandidateIndexes
{
    internal required List<EntityNode> ExistingNodes { get; set; }

    internal required OrderedDictionary<string, EntityNode> NodesByUuid { get; set; }

    internal required OrderedDictionary<string, List<EntityNode>> NormalizedExisting { get; set; }

    internal required OrderedDictionary<string, HashSet<string>> ShinglesByCandidate { get; set; }

    internal required OrderedDictionary<PyTuple, List<string>> LshBuckets { get; set; }
}

internal sealed record DedupResolutionState
{
    internal required List<EntityNode?> ResolvedNodes { get; set; }

    internal required OrderedDictionary<string, string> UuidMap { get; set; }

    internal required List<long> UnresolvedIndices { get; set; }

    internal List<(EntityNode, EntityNode)> DuplicatePairs { get; set; } = [];
}

internal static partial class DedupHelpers
{
    // TODO(port): _promote_resolved_node
    internal static EntityNode PromoteResolvedNode(EntityNode extractedNode, EntityNode resolvedNode)
    {
        throw new NotSupportedException("TODO(port): _promote_resolved_node");
    }

    // TODO(port): _merge_non_empty_attributes
    internal static void MergeNonEmptyAttributes(EntityNode resolvedNode, EntityNode extractedNode)
    {
        throw new NotSupportedException("TODO(port): _merge_non_empty_attributes");
    }

    // TODO(port): _identity_type_for_node
    internal static string? IdentityTypeForNode(
        EntityNode node,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        throw new NotSupportedException("TODO(port): _identity_type_for_node");
    }

    // PORT: `value: object` is a value of open data, a node attribute (R6).
    // TODO(port): _normalize_identity_value
    internal static object? NormalizeIdentityValue(object? value)
    {
        throw new NotSupportedException("TODO(port): _normalize_identity_value");
    }

    // TODO(port): no rule for a tuple that code compares with `==` and that holds a value of open data. The result
    // `tuple[str, tuple[tuple[str, object], ...]] | None` is a PyTuple of the identity type and a PyTuple of
    // (name, value) PyTuples, so that `==` has the value equality of Python (R6, R14).
    // TODO(port): _identity_key_for_node
    internal static PyTuple? IdentityKeyForNode(
        EntityNode node,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        throw new NotSupportedException("TODO(port): _identity_key_for_node");
    }

    // TODO(port): _resolve_with_identity_properties
    internal static void ResolveWithIdentityProperties(
        List<EntityNode> extractedNodes,
        DedupCandidateIndexes indexes,
        DedupResolutionState state,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        throw new NotSupportedException("TODO(port): _resolve_with_identity_properties");
    }

    // TODO(port): _build_candidate_indexes
    internal static DedupCandidateIndexes BuildCandidateIndexes(List<EntityNode> existingNodes)
    {
        throw new NotSupportedException("TODO(port): _build_candidate_indexes");
    }

    // PORT: `candidate_ids` is iterated with no sort, so it is a PySet<string> in insertion order (R14).
    // TODO(port): _resolve_with_similarity
    internal static void ResolveWithSimilarity(
        List<EntityNode> extractedNodes,
        DedupCandidateIndexes indexes,
        DedupResolutionState state)
    {
        throw new NotSupportedException("TODO(port): _resolve_with_similarity");
    }

    // PORT: `__all__` only lists names, so it has no member (R1).
}
