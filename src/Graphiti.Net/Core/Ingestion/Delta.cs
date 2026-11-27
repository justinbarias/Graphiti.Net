// Ported from graphiti_core/ingestion/delta.py
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

namespace Graphiti.Core.Ingestion;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

// PORT: the module name equals the class name Delta, so the module class has the suffix Module (R2).
// PORT: a float32 `np.ndarray` matrix is a `float[,]`. It keeps the shape (0, 0) of `np.zeros((0, 0))` and the width
// that `_stack` and `_top_matches` compare. R18 says that the lead adds the float32 sum form with this unit.
internal static partial class DeltaModule
{
    // The number of delta units for which the process keeps a digest.
    internal const long DigestCacheSize = 1024;

    // PORT: a float32 numpy matrix is a `float[,]` (R18).
    // PORT: internal, because judge cases call it (R2).
    // TODO(port): _unit_matrix
    internal static float[,] UnitMatrix(List<List<double>> vectors)
    {
        throw new NotSupportedException("TODO(port): _unit_matrix");
    }

    // TODO(port): patch_as_node
    internal static EntityNode PatchAsNode(EntityNode node, PatchNode patch)
    {
        throw new NotSupportedException("TODO(port): patch_as_node");
    }

    // PORT: `tuple[set[str], list[tuple[int, ...]]] | None`. A band is a `tuple[int, ...]` that is a part of a dict key
    // of `lsh_buckets`, so it is a PyTuple (R14). The shingles are a set that code tests only (R14).
    // PORT: internal, because judge cases call it (R2).
    // TODO(port): _fuzzy_query
    internal static (HashSet<string> Shingles, List<PyTuple> Bands)? FuzzyQuery(string name)
    {
        throw new NotSupportedException("TODO(port): _fuzzy_query");
    }

    // TODO(port): _fuzzy_match
    private static string? FuzzyMatch((HashSet<string> Shingles, List<PyTuple> Bands)? query, DedupCandidateIndexes indexes)
    {
        throw new NotSupportedException("TODO(port): _fuzzy_match");
    }
}

// PORT: the private dataclass `_UnitDigest`. internal, because Delta reads it (R2).
internal sealed record UnitDigest
{
    internal required long Seq { get; set; }

    internal required string Status { get; set; }

    internal required List<EntityNode> Nodes { get; set; }

    internal required DedupCandidateIndexes NodeIndexes { get; set; }

    internal required List<string> NodeVectorUuids { get; set; }

    internal required float[,] NodeVectors { get; set; }

    internal required List<(string, string, string)> Edges { get; set; }

    internal required List<string> EdgeVectorUuids { get; set; }

    internal required float[,] EdgeVectors { get; set; }

    internal required List<PatchNode> Patches { get; set; }
}

internal static partial class DeltaModule
{
    // TODO(port): _digest
    private static UnitDigest Digest(DeltaUnit unit)
    {
        throw new NotSupportedException("TODO(port): _digest");
    }

    // PORT: an OrderedDict with move_to_end and popitem(last=False) (R14). `hash(unit.payload)` in the key is the
    // SHA-256 of the bytes as a hex string (R18).
    private static readonly OrderedDictionary<(string, long, string, long, string), UnitDigest> DigestCache = new();

    // TODO(port): load_delta_digests
    internal static Delta LoadDeltaDigests(string groupId, List<DeltaUnit> units)
    {
        throw new NotSupportedException("TODO(port): load_delta_digests");
    }

    // TODO(port): _stack
    private static float[,] Stack(List<float[,]> matrices)
    {
        throw new NotSupportedException("TODO(port): _stack");
    }

    // TODO(port): _stack_uuids
    private static List<string> StackUuids(List<List<string>> uuidLists, List<float[,]> matrices)
    {
        throw new NotSupportedException("TODO(port): _stack_uuids");
    }

    // PORT: internal, because Delta calls it (R2).
    // TODO(port): _top_matches
    internal static List<List<string>> TopMatches(
        List<List<double>?> vectors,
        List<string> candidateUuids,
        float[,] candidateVectors,
        double minScore,
        long limit)
    {
        throw new NotSupportedException("TODO(port): _top_matches");
    }
}

// PORT: a dataclass with `__post_init__`: the code that builds the value calls Validate() (R7). `patches` and
// `created_node_uuids` have `init=False`, and Validate() sets them.
internal sealed record Delta
{
    internal List<UnitDigest> Digests { get; set; } = [];

    internal OrderedDictionary<string, List<PatchNode>> Patches { get; set; } = new();

    // PORT: a set that code tests with `in` only (R14).
    internal HashSet<string> CreatedNodeUuids { get; set; } = new(StringComparer.Ordinal);

    // TODO(port): Delta.__post_init__
    internal Delta Validate()
    {
        throw new NotSupportedException("TODO(port): Delta.__post_init__");
    }

    // TODO(port): Delta.unit_count
    internal long UnitCount => throw new NotSupportedException("TODO(port): Delta.unit_count");

    // TODO(port): Delta._patched_copy
    private EntityNode PatchedCopy(EntityNode node)
    {
        throw new NotSupportedException("TODO(port): Delta._patched_copy");
    }

    // TODO(port): Delta.match_nodes
    internal List<List<string>> MatchNodes(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties = null)
    {
        throw new NotSupportedException("TODO(port): Delta.match_nodes");
    }

    // TODO(port): Delta.patched_identity_matches
    internal List<List<string>> PatchedIdentityMatches(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        throw new NotSupportedException("TODO(port): Delta.patched_identity_matches");
    }

    // TODO(port): Delta.edges_between
    internal List<List<string>> EdgesBetween(List<EntityEdge> edges)
    {
        throw new NotSupportedException("TODO(port): Delta.edges_between");
    }

    // TODO(port): Delta.similar_edges
    internal List<List<string>> SimilarEdges(List<EntityEdge> edges)
    {
        throw new NotSupportedException("TODO(port): Delta.similar_edges");
    }
}
