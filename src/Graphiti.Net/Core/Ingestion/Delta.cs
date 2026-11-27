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

using System.Security.Cryptography;
using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

// PORT: the module name equals the class name Delta, so the module class has the suffix Module (R2).
// PORT: a float32 `np.ndarray` matrix is a `float[,]`. It keeps the shape (0, 0) of `np.zeros((0, 0))` and the width
// that `_stack` and `_top_matches` compare. Each value that depends on float32 rounding is computed in `float`, in
// the order of the numpy operation (R18).
internal static partial class DeltaModule
{
    // The number of delta units for which the process keeps a digest.
    internal const long DigestCacheSize = 1024;

    // PORT: a float32 numpy matrix is a `float[,]` (R18).
    // PORT: internal, because judge cases call it (R2).

    /// <summary>Return the vectors as rows with a length of 1.</summary>
    internal static float[,] UnitMatrix(List<List<double>> vectors)
    {
        // PORT: `np.asarray(vectors, dtype=np.float32)`. An empty list has the shape (0,) and ndim 1. Rows of
        // different lengths raise the ValueError of numpy.
        if (vectors.Count == 0)
        {
            return new float[0, 0];
        }

        var width = vectors[0].Count;
        if (vectors.Any(vector => vector.Count != width))
        {
            throw new ArgumentException(
                "setting an array element with a sequence. The requested array has an inhomogeneous shape after 1 dimensions. "
                + $"The detected shape was ({vectors.Count},) + inhomogeneous part.");
        }

        var matrix = new float[vectors.Count, width];
        for (var row = 0; row < vectors.Count; row++)
        {
            for (var column = 0; column < width; column++)
            {
                matrix[row, column] = (float)vectors[row][column];
            }
        }

        // PORT: `np.linalg.norm(matrix, axis=1, keepdims=True)` is `sqrt(add.reduce(x * x, axis=1))` in float32.
        // numpy sums each row in pairs (`FLOAT_pairwise_sum`), from the identity 0. PyNumpy.AddReduce has only the
        // float64 form, so the float32 form is a local function here (R18: the lead adds the float32 form).
        static float PairwiseSum(float[] a, int start, int n)
        {
            if (n < 8)
            {
                var sum = 0.0f;
                for (var i = 0; i < n; i++)
                {
                    sum += a[start + i];
                }

                return sum;
            }

            if (n <= 128)
            {
                var r = new float[8];
                for (var j = 0; j < 8; j++)
                {
                    r[j] = a[start + j];
                }

                int k;
                for (k = 8; k < n - (n % 8); k += 8)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        r[j] += a[start + k + j];
                    }
                }

                var res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
                for (; k < n; k++)
                {
                    res += a[start + k];
                }

                return res;
            }

            var half = n / 2;
            half -= half % 8;
            return PairwiseSum(a, start, half) + PairwiseSum(a, start + half, n - half);
        }

        var norms = new float[vectors.Count];
        var squares = new float[width];
        for (var row = 0; row < vectors.Count; row++)
        {
            for (var column = 0; column < width; column++)
            {
                squares[column] = matrix[row, column] * matrix[row, column];
            }

            norms[row] = MathF.Sqrt(0.0f + PairwiseSum(squares, 0, width));
        }

        for (var row = 0; row < norms.Length; row++)
        {
            if (norms[row] == 0)
            {
                norms[row] = 1.0f;
            }
        }

        var result = new float[vectors.Count, width];
        for (var row = 0; row < vectors.Count; row++)
        {
            for (var column = 0; column < width; column++)
            {
                result[row, column] = matrix[row, column] / norms[row];
            }
        }

        return result;
    }

    /// <summary>Return the labels and the attributes of a patch as a node, for `_promote_resolved_node`.</summary>
    internal static EntityNode PatchAsNode(EntityNode node, PatchNode patch)
    {
        return new EntityNode
        {
            Uuid = node.Uuid,
            Name = node.Name,
            GroupId = node.GroupId,
            Labels = [.. patch.Labels],
            Attributes = new OrderedDictionary<string, object?>(patch.Attributes),
        }.Validate();
    }

    // PORT: `tuple[set[str], list[tuple[int, ...]]] | None`. A band is a `tuple[int, ...]` that is a part of a dict key
    // of `lsh_buckets`, so it is a PyTuple (R14). The shingles are a set that code tests only (R14).
    // PORT: internal, because judge cases call it (R2).

    /// <summary>Return the shingles and the LSH bands of a name, or None when the name has low entropy.</summary>
    internal static (HashSet<string> Shingles, List<PyTuple> Bands)? FuzzyQuery(string name)
    {
        var normalized = DedupHelpers.NormalizeNameForFuzzy(name);
        if (!DedupHelpers.HasHighEntropy(normalized))
        {
            return null;
        }

        var shingles = DedupHelpers.CachedShingles(normalized);
        return (shingles, [.. DedupHelpers.LshBands(DedupHelpers.MinhashSignature(shingles))]);
    }

    // PORT: `candidate_ids` is iterated with no sort, so it is a PySet<string> in insertion order (R14).
    // PORT: internal, because Delta calls it (R2).

    /// <summary>
    /// Return the UUID that the fuzzy rule of `_resolve_with_similarity` selects.
    ///
    /// The function changes no node.
    /// </summary>
    internal static string? FuzzyMatch((HashSet<string> Shingles, List<PyTuple> Bands)? query, DedupCandidateIndexes indexes)
    {
        if (query is null)
        {
            return null;
        }

        var (shingles, bands) = query.Value;
        var candidateIds = new PySet<string>(comparer: StringComparer.Ordinal);
        foreach (var (bandIndex, band) in bands.Index())
        {
            foreach (var candidateId in indexes.LshBuckets.TryGetValue(new PyTuple((long)bandIndex, band), out var bucket) ? bucket : [])
            {
                candidateIds.Add(candidateId);
            }
        }

        string? bestUuid = null;
        var bestScore = 0.0;
        foreach (var candidateId in candidateIds)
        {
            var score = DedupHelpers.JaccardSimilarity(
                shingles,
                indexes.ShinglesByCandidate.TryGetValue(candidateId, out var stored) ? stored : new HashSet<string>(StringComparer.Ordinal));
            if (score > bestScore)
            {
                bestScore = score;
                bestUuid = candidateId;
            }
        }

        return bestScore >= DedupHelpers.FuzzyJaccardThreshold ? bestUuid : null;
    }
}

// PORT: the private dataclass `_UnitDigest`. internal, because Delta reads it (R2).

/// <summary>
/// The parts of one final change set that the repair compares with a new unit.
///
/// A digest has no full record. The repair reads a record that matches from the graph.
/// </summary>
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
    private static UnitDigest Digest(DeltaUnit unit)
    {
        var changeSet = ChangeSet.FromBytes(unit.Payload);
        var nodes = changeSet.CreateNodes
            .Select(node =>
            {
                var copy = node.ModelCopy();
                copy.NameEmbedding = null;
                copy.Summary = string.Empty;
                return copy;
            })
            .ToList();
        var nodesWithVector = changeSet.CreateNodes.Where(node => node.NameEmbedding is { Count: > 0 }).ToList();
        var edgesWithVector = changeSet.CreateEdges.Where(edge => edge.FactEmbedding is { Count: > 0 }).ToList();
        return new UnitDigest
        {
            Seq = unit.Seq,
            Status = unit.Status,
            Nodes = nodes,
            NodeIndexes = DedupHelpers.BuildCandidateIndexes(nodes),
            NodeVectorUuids = [.. nodesWithVector.Select(node => node.Uuid)],
            NodeVectors = UnitMatrix([.. nodesWithVector.Select(node => node.NameEmbedding ?? [])]),
            Edges = [.. changeSet.CreateEdges.Select(edge => (edge.Uuid, edge.SourceNodeUuid, edge.TargetNodeUuid))],
            EdgeVectorUuids = [.. edgesWithVector.Select(edge => edge.Uuid)],
            EdgeVectors = UnitMatrix([.. edgesWithVector.Select(edge => edge.FactEmbedding ?? [])]),
            Patches = [.. changeSet.Operations.OfType<PatchNode>()],
        };
    }

    // PORT: an OrderedDict with move_to_end and popitem(last=False) (R14). `hash(unit.payload)` in the key is the
    // SHA-256 of the bytes as a hex string (R18).
    private static readonly OrderedDictionary<(string, long, string, long, string), UnitDigest> DigestCache = new();

    /// <summary>
    /// Return the delta of a commit from the final change sets that the store returned.
    ///
    /// The final change set of a unit does not change. The process thus keeps the digest
    /// of each payload, and a later commit does not decode the payload again.
    /// </summary>
    internal static Delta LoadDeltaDigests(string groupId, List<DeltaUnit> units)
    {
        List<UnitDigest> digests = [];
        foreach (var unit in units.OrderBy(unit => unit.Seq))
        {
            var key = (groupId, unit.Seq, unit.Status, (long)unit.Payload.Length, Convert.ToHexStringLower(SHA256.HashData(unit.Payload)));
            if (!DigestCache.TryGetValue(key, out var digest))
            {
                digest = Digest(unit);
                DigestCache[key] = digest;
                while (DigestCache.Count > DigestCacheSize)
                {
                    DigestCache.RemoveAt(0);
                }
            }
            else
            {
                DigestCache.Remove(key);
                DigestCache.Add(key, digest);
            }

            digests.Add(digest);
        }

        return new Delta { Digests = digests }.Validate();
    }

    // PORT: internal, because Delta calls it (R2).
    internal static float[,] Stack(List<float[,]> matrices)
    {
        matrices = [.. matrices.Where(matrix => matrix.Length != 0)];
        if (matrices.Count == 0)
        {
            return new float[0, 0];
        }

        var width = matrices[0].GetLength(1);
        var kept = matrices.Where(matrix => matrix.GetLength(1) == width).ToList();
        var result = new float[kept.Sum(matrix => matrix.GetLength(0)), width];
        var offset = 0;
        foreach (var matrix in kept)
        {
            for (var row = 0; row < matrix.GetLength(0); row++)
            {
                for (var column = 0; column < width; column++)
                {
                    result[offset + row, column] = matrix[row, column];
                }
            }

            offset += matrix.GetLength(0);
        }

        return result;
    }

    // PORT: internal, because Delta calls it (R2).
    internal static List<string> StackUuids(List<List<string>> uuidLists, List<float[,]> matrices)
    {
        var matricesWithRows = Py.ZipStrict(uuidLists, matrices).Where(pair => pair.Second.Length != 0).ToList();
        if (matricesWithRows.Count == 0)
        {
            return [];
        }

        var width = matricesWithRows[0].Second.GetLength(1);
        return [.. matricesWithRows.Where(pair => pair.Second.GetLength(1) == width).SelectMany(pair => pair.First)];
    }

    // PORT: internal, because Delta calls it (R2).

    /// <summary>Return, for each vector, the candidates with a cosine similarity of `min_score` or more.</summary>
    internal static List<List<string>> TopMatches(
        List<List<double>?> vectors,
        List<string> candidateUuids,
        float[,] candidateVectors,
        double minScore,
        long limit)
    {
        List<List<string>> results = [.. vectors.Select(_ => new List<string>())];
        List<int> rows = [.. vectors.Index().Where(pair => pair.Item is { Count: > 0 }).Select(pair => pair.Index)];
        if (rows.Count == 0 || candidateUuids.Count == 0)
        {
            return results;
        }

        var queries = UnitMatrix([.. rows.Select(index => vectors[index] ?? [])]);
        if (queries.GetLength(1) != candidateVectors.GetLength(1))
        {
            return results;
        }

        // PORT: `queries @ candidate_vectors.T` is a plain loop in index order with a float32 sum (R18). BLAS sums in
        // another order, so a score can differ in its last bits (GAPS G17).
        var width = queries.GetLength(1);
        var candidates = candidateVectors.GetLength(0);
        var scores = new float[queries.GetLength(0), candidates];
        for (var row = 0; row < queries.GetLength(0); row++)
        {
            for (var column = 0; column < candidates; column++)
            {
                var sum = 0.0f;
                for (var k = 0; k < width; k++)
                {
                    sum += queries[row, k] * candidateVectors[column, k];
                }

                scores[row, column] = sum;
            }
        }

        // PORT: NumPy 2 compares a float32 score with a Python float in float32 (NEP 50).
        var threshold = (float)minScore;
        foreach (var (row, index) in rows.Index())
        {
            // PORT: `np.argsort(-scores[row])`. numpy puts nan last. A stable sort keeps equal scores in column order,
            // as the insertion sort of numpy does for 16 candidates or fewer. Above 16, numpy's quicksort can give
            // equal scores in another order.
            var order = Enumerable.Range(0, candidates)
                .OrderBy(
                    column => -scores[row, column],
                    Comparer<float>.Create((a, b) => float.IsNaN(a) ? (float.IsNaN(b) ? 0 : 1) : float.IsNaN(b) ? -1 : a.CompareTo(b)))
                .ToList();
            results[index] = [.. Py.Slice(order, null, limit)
                .Where(column => scores[row, column] >= threshold)
                .Select(column => candidateUuids[column])];
        }

        return results;
    }
}

// PORT: a dataclass with `__post_init__`: the code that builds the value calls Validate() (R7). `patches` and
// `created_node_uuids` have `init=False`, and Validate() sets them.

/// <summary>The records and the patches of the units that committed after a snapshot.</summary>
internal sealed record Delta
{
    internal List<UnitDigest> Digests { get; set; } = [];

    internal OrderedDictionary<string, List<PatchNode>> Patches { get; set; } = new();

    // PORT: a set that code tests with `in` only (R14).
    internal HashSet<string> CreatedNodeUuids { get; set; } = new(StringComparer.Ordinal);

    internal Delta Validate()
    {
        Patches = new(StringComparer.Ordinal);
        CreatedNodeUuids = new(StringComparer.Ordinal);
        foreach (var digest in Digests)
        {
            foreach (var patch in digest.Patches)
            {
                Py.SetDefault(Patches, patch.NodeUuid, []).Add(patch);
            }

            CreatedNodeUuids.UnionWith(digest.Nodes.Select(node => node.Uuid));
        }

        return this;
    }

    internal long UnitCount => Digests.Count;

    private EntityNode PatchedCopy(EntityNode node)
    {
        var patches = Patches.TryGetValue(node.Uuid, out var found) ? found : null;
        if (patches is null || patches.Count == 0)
        {
            return node;
        }

        var copy = node.ModelCopy(deep: true);
        foreach (var patch in patches)
        {
            DedupHelpers.PromoteResolvedNode(DeltaModule.PatchAsNode(copy, patch), copy);
        }

        return copy;
    }

    /// <summary>
    /// Return, for each node, the UUIDs of the delta nodes that can be the same entity.
    ///
    /// A delta node matches by the same normalized name, the same identity key, a fuzzy
    /// name match, or a cosine similarity of the name embeddings of
    /// `NODE_DEDUP_COSINE_MIN_SCORE` or more. A match by one of the first three rules
    /// comes first. The result has at most `NODE_DEDUP_CANDIDATE_LIMIT` UUIDs for a node.
    /// </summary>
    internal List<List<string>> MatchNodes(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties = null)
    {
        List<List<string>> results = [.. nodes.Select(_ => new List<string>())];
        if (Digests.Count == 0)
        {
            return results;
        }

        foreach (var (index, node) in nodes.Index())
        {
            var normalizedName = DedupHelpers.NormalizeStringExact(node.Name);
            var identityKey = DedupHelpers.IdentityKeyForNode(node, identityProperties);
            var fuzzyQuery = DeltaModule.FuzzyQuery(node.Name);
            foreach (var digest in Digests)
            {
                foreach (var candidate in digest.NodeIndexes.NormalizedExisting.TryGetValue(normalizedName, out var existing) ? existing : [])
                {
                    results[index].Add(candidate.Uuid);
                }

                if (identityKey is not null)
                {
                    foreach (var candidate in digest.Nodes)
                    {
                        var patched = PatchedCopy(candidate);
                        if (identityKey.Equals(DedupHelpers.IdentityKeyForNode(patched, identityProperties)))
                        {
                            results[index].Add(candidate.Uuid);
                        }
                    }
                }

                var fuzzyMatch = DeltaModule.FuzzyMatch(fuzzyQuery, digest.NodeIndexes);
                if (fuzzyMatch is not null)
                {
                    results[index].Add(fuzzyMatch);
                }
            }
        }

        var similar = DeltaModule.TopMatches(
            [.. nodes.Select(node => node.NameEmbedding)],
            DeltaModule.StackUuids(
                [.. Digests.Select(digest => digest.NodeVectorUuids)],
                [.. Digests.Select(digest => digest.NodeVectors)]),
            DeltaModule.Stack([.. Digests.Select(digest => digest.NodeVectors)]),
            NodeOperations.NodeDedupCosineMinScore,
            Nodes.NodeDedupCandidateLimit);
        var ownUuids = nodes.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);
        List<List<string>> matches = [];
        foreach (var (ruleMatches, similarMatches) in Py.ZipStrict(results, similar))
        {
            // PORT: `dict.fromkeys(...)` keeps the first of each UUID in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> unique = [.. ruleMatches.Concat(similarMatches).Where(seen.Add)];
            matches.Add(Py.Slice(unique.Where(uuid => !ownUuids.Contains(uuid)).ToList(), null, Nodes.NodeDedupCandidateLimit));
        }

        return matches;
    }

    /// <summary>Return, for each node, the stored nodes that a patch of the delta gave its identity key.</summary>
    internal List<List<string>> PatchedIdentityMatches(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties)
    {
        List<List<string>> results = [.. nodes.Select(_ => new List<string>())];
        if (identityProperties is null || identityProperties.Count == 0 || Patches.Count == 0)
        {
            return results;
        }

        var keys = nodes.Select(node => DedupHelpers.IdentityKeyForNode(node, identityProperties)).ToList();
        if (!keys.Any(key => key is not null))
        {
            return results;
        }

        foreach (var nodeUuid in Patches.Keys)
        {
            if (CreatedNodeUuids.Contains(nodeUuid))
            {
                continue;
            }

            var patched = PatchedCopy(
                new EntityNode { Uuid = nodeUuid, Name = string.Empty, GroupId = nodes[0].GroupId, Labels = [] }.Validate());
            var patchedKey = DedupHelpers.IdentityKeyForNode(patched, identityProperties);
            if (patchedKey is null)
            {
                continue;
            }

            foreach (var (index, key) in keys.Index())
            {
                if (patchedKey.Equals(key))
                {
                    results[index].Add(nodeUuid);
                }
            }
        }

        return results;
    }

    /// <summary>Return, for each edge, the delta edges between its two nodes, in either direction.</summary>
    internal List<List<string>> EdgesBetween(List<EntityEdge> edges)
    {
        // PORT: a dict keyed by a frozenset that code reads only by key (R14). A frozen PySet has value equality
        // with PyEqualityComparer.
        var byPair = new Dictionary<PySet<string>, List<string>>(PyEqualityComparer<PySet<string>>.Instance);
        foreach (var digest in Digests)
        {
            foreach (var (edgeUuid, sourceUuid, targetUuid) in digest.Edges)
            {
                Py.SetDefault(byPair, new PySet<string>([sourceUuid, targetUuid], StringComparer.Ordinal) { Frozen = true }, []).Add(edgeUuid);
            }
        }

        return
        [
            .. edges.Select(edge => byPair.TryGetValue(
                new PySet<string>([edge.SourceNodeUuid, edge.TargetNodeUuid], StringComparer.Ordinal) { Frozen = true },
                out var found) ? new List<string>(found) : []),
        ];
    }

    /// <summary>
    /// Return, for each edge, the delta edges with a similar fact embedding.
    ///
    /// The limits are those of the graph-wide search: a cosine similarity of
    /// `DEFAULT_MIN_SCORE` or more, and at most `DEFAULT_SEARCH_LIMIT` edges.
    /// </summary>
    internal List<List<string>> SimilarEdges(List<EntityEdge> edges)
    {
        return DeltaModule.TopMatches(
            [.. edges.Select(edge => edge.FactEmbedding)],
            DeltaModule.StackUuids(
                [.. Digests.Select(digest => digest.EdgeVectorUuids)],
                [.. Digests.Select(digest => digest.EdgeVectors)]),
            DeltaModule.Stack([.. Digests.Select(digest => digest.EdgeVectors)]),
            SearchUtils.DefaultMinScore,
            SearchConfigModule.DefaultSearchLimit);
    }
}
