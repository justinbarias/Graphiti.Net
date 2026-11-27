namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/dedup_helpers.py</c>.</summary>
internal static class DedupHelpersBindings
{
    private const string Module = "graphiti_core.utils.maintenance.dedup_helpers";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":_normalize_string_exact", call => call.Signature("name").InvokeAsync(DedupHelpers.NormalizeStringExact));
        table.Pure.Add(Module + ":_normalize_name_for_fuzzy", call => call.Signature("name").InvokeAsync(DedupHelpers.NormalizeNameForFuzzy));
        table.Pure.Add(Module + ":_name_entropy", call => call.Signature("normalized_name").InvokeAsync(DedupHelpers.NameEntropy));
        table.Pure.Add(Module + ":_has_high_entropy", call => call.Signature("normalized_name").InvokeAsync(DedupHelpers.HasHighEntropy));
        table.Pure.Add(Module + ":_shingles", call => call.Signature("normalized_name").InvokeAsync(DedupHelpers.Shingles));
        table.Pure.Add(Module + ":_hash_shingle", call => call.Signature("shingle", "seed").InvokeAsync(DedupHelpers.HashShingle));
        table.Pure.Add(Module + ":_minhash_signature", call => call.Signature("shingles").InvokeAsync(DedupHelpers.MinhashSignature));
        table.Pure.Add(Module + ":_lsh_bands", call => call.Signature("signature").InvokeAsync(DedupHelpers.LshBands));
        table.Pure.Add(Module + ":_jaccard_similarity", call => call.Signature("a", "b").InvokeAsync(DedupHelpers.JaccardSimilarity));
        table.Pure.Add(Module + ":_cached_shingles", call => call.Signature("name").InvokeAsync(DedupHelpers.CachedShingles));
        table.Pure.Add(Module + ":_promote_resolved_node", call => call.Signature("extracted_node", "resolved_node").InvokeAsync(DedupHelpers.PromoteResolvedNode));
        table.Pure.Add(Module + ":_identity_type_for_node", call => call.Signature("node", "identity_properties").InvokeAsync(DedupHelpers.IdentityTypeForNode));
        table.Pure.Add(Module + ":_normalize_identity_value", call => call.Signature("value").InvokeAsync(DedupHelpers.NormalizeIdentityValue));
        table.Pure.Add(Module + ":_identity_key_for_node", call => call.Signature("node", "identity_properties").InvokeAsync(DedupHelpers.IdentityKeyForNode));
        table.Pure.Add(Module + ":_build_candidate_indexes", call => call.Signature("existing_nodes").InvokeAsync(DedupHelpers.BuildCandidateIndexes));

        // A dataclass in a result is a $type with its Python field names (R22).
        table.Registry.AddType<DedupCandidateIndexes>("DedupCandidateIndexes", m =>
        [
            new("existing_nodes", m.ExistingNodes),
            new("nodes_by_uuid", m.NodesByUuid),
            new("normalized_existing", m.NormalizedExisting),
            new("shingles_by_candidate", m.ShinglesByCandidate),
            new("lsh_buckets", m.LshBuckets),
        ]);
    }
}
